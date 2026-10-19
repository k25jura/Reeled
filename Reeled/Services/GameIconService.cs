using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

/// <summary>A Steam store search result.</summary>
public sealed record SteamGame(int AppId, string Name, string? CapsuleUrl);

/// <summary>
/// Game icons for clip folders (Shadowplay, OBS etc. name folders after the game). Folder names are
/// matched against the Steam store; only confident matches are applied automatically, everything else
/// can be picked by hand. Icons are cached on disk and keyed by folder name, so the same game folder in
/// different watch roots shares one icon.
/// </summary>
public static partial class GameIconService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reeled", "GameIcons");

    /// <summary>Folders that are never games (desktop recordings, tools...).</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop", "videos", "captures", "clips", "recordings", "replays", "highlights", "screen recordings",
        "nvidia", "shadowplay", "obs", "temp", "other", "misc", "new folder"
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Reeled/2.0 (+https://github.com/k25jura/Reeled)");
        return client;
    }

    public static string KeyFor(string folderName) => folderName.Trim().ToLowerInvariant();

    // ---------------------------------------------------------------------------------------------
    // Matching
    // ---------------------------------------------------------------------------------------------

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWordRegex();

    private static readonly string[] NoiseWords = { "clips", "clip", "replays", "replay", "highlights", "recordings", "videos", "captures" };
    private static readonly string[] EditionWords =
    {
        "enhanced", "legacy", "remastered", "definitive edition", "game of the year edition", "goty",
        "directors cut", "director s cut", "complete edition", "deluxe edition", "anniversary edition", "edition"
    };

    /// <summary>Lower-case words only: "Counter-Strike® 2 Clips (2024)" → "counter strike 2".</summary>
    public static string Normalize(string name, bool stripEditions = false)
    {
        string s = name.ToLowerInvariant().Replace("&", " and ").Replace("'", "");
        s = YearRegex().Replace(s, " ");
        s = NonWordRegex().Replace(s, " ");
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !NoiseWords.Contains(w)).ToList();
        s = string.Join(' ', words);
        if (stripEditions)
        {
            foreach (var e in EditionWords)
            {
                if (s.EndsWith(" " + e)) s = s[..^(e.Length + 1)];
            }
        }
        return s.Trim();
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) d[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = d[0];
            d[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int temp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = temp;
            }
        }
        return d[b.Length];
    }

    /// <summary>0..1, 1 = same name (ignoring case, punctuation, spacing and edition suffixes).</summary>
    public static double Similarity(string folder, string game)
    {
        string a = Normalize(folder, stripEditions: true);
        string b = Normalize(game, stripEditions: true);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b || a.Replace(" ", "") == b.Replace(" ", "")) return 1;
        int max = Math.Max(a.Length, b.Length);
        return 1.0 - (double)Levenshtein(a, b) / max;
    }

    /// <summary>The search result that clearly is this folder's game, or null when unsure.</summary>
    public static SteamGame? PickConfident(string folderName, IReadOnlyList<SteamGame> results)
    {
        SteamGame? best = null;
        double bestScore = 0;
        foreach (var r in results.Take(8))
        {
            double score = Similarity(folderName, r.Name);
            if (score > bestScore) { bestScore = score; best = r; }
        }
        // Short names need an exact match ("Rust" must not become "Rusty Lake")
        double required = Normalize(folderName).Length <= 5 ? 1.0 : 0.9;
        return bestScore >= required ? best : null;
    }

    public static bool LooksLikeGame(string folderName)
    {
        string n = Normalize(folderName);
        return n.Length >= 2 && !NotGames.Contains(n) && !NotGames.Contains(folderName.Trim());
    }

    // ---------------------------------------------------------------------------------------------
    // Steam
    // ---------------------------------------------------------------------------------------------

    public static async Task<List<SteamGame>> SearchAsync(string term, CancellationToken ct = default)
    {
        var results = new List<SteamGame>();
        if (string.IsNullOrWhiteSpace(term)) return results;
        string url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(term.Trim())}&l=english&cc=US";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("items", out var items)) return results;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) && type.GetString() != "app") continue;
            int id = item.GetProperty("id").GetInt32();
            string name = item.GetProperty("name").GetString() ?? string.Empty;
            string? img = item.TryGetProperty("tiny_image", out var t) ? t.GetString() : null;
            results.Add(new SteamGame(id, name, img));
        }
        return results;
    }

    /// <summary>Downloads the game's square community icon (or a capsule as fallback) into the cache.</summary>
    public static async Task<string?> DownloadIconAsync(int appId, CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheFolder);
        string target = Path.Combine(CacheFolder, $"{appId}.jpg");
        if (File.Exists(target) && new FileInfo(target).Length > 0) return target;

        string? iconUrl = null;
        try
        {
            string input = $"{{\"ids\":[{{\"appid\":{appId}}}],\"context\":{{\"language\":\"english\",\"country_code\":\"US\",\"steam_realm\":1}},\"data_request\":{{\"include_assets\":true}}}}";
            string url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(input);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
            var item = doc.RootElement.GetProperty("response").GetProperty("store_items")[0];
            if (item.TryGetProperty("assets", out var assets))
            {
                if (assets.TryGetProperty("community_icon", out var icon) && !string.IsNullOrEmpty(icon.GetString()))
                {
                    iconUrl = $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{icon.GetString()}.jpg";
                }
                else if (assets.TryGetProperty("asset_url_format", out var format) && assets.TryGetProperty("small_capsule", out var capsule))
                {
                    iconUrl = "https://shared.cloudflare.steamstatic.com/store_item_assets/" +
                              format.GetString()!.Replace("${FILENAME}", capsule.GetString());
                }
            }
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("GameIcon assets", ex.Message);
        }
        iconUrl ??= $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/capsule_sm_120.jpg";

        var bytes = await Http.GetByteArrayAsync(iconUrl, ct);
        await File.WriteAllBytesAsync(target, bytes, ct);
        return target;
    }

    /// <summary>
    /// Finds an icon for a folder automatically. Returns null when nothing confident turned up (that's
    /// remembered too, so the store isn't asked again for a while).
    /// </summary>
    public static async Task<GameIconInfo> LookUpAsync(string folderName, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var info = new GameIconInfo { Source = GameIconSource.None, CheckedUtc = DateTime.UtcNow };
            var results = await SearchAsync(Normalize(folderName), ct);
            var match = PickConfident(folderName, results);
            if (match == null) return info;
            info.ImagePath = await DownloadIconAsync(match.AppId, ct);
            info.AppId = match.AppId;
            info.GameName = match.Name;
            info.Source = info.ImagePath != null ? GameIconSource.Steam : GameIconSource.None;
            // Be gentle with the store between lookups
            await Task.Delay(350, ct);
            return info;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Stores a user-chosen image (or a program's icon) in the cache.</summary>
    public static async Task<string?> CacheCustomImageAsync(Windows.Storage.StorageFile file, string folderKey)
    {
        Directory.CreateDirectory(CacheFolder);
        string safe = string.Concat(folderKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string target = Path.Combine(CacheFolder, $"custom-{safe}-{DateTime.UtcNow:yyyyMMddHHmmss}.png");
        string ext = file.FileType.ToLowerInvariant();
        if (ext is ".exe" or ".lnk" or ".ico")
        {
            // A program's own icon, as Explorer shows it
            using var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 64);
            if (thumb == null) return null;
            using var output = File.Create(target);
            await thumb.AsStreamForRead().CopyToAsync(output);
            return target;
        }
        await file.CopyAsync(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(CacheFolder), Path.GetFileName(target),
            Windows.Storage.NameCollisionOption.ReplaceExisting);
        return target;
    }
}
