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

/// <summary>
/// Game icons for clip folders (Shadowplay, OBS etc. name folders after the game). Folder names are
/// matched against Steam, then SteamGridDB and IGDB when those are set up; only confident matches are
/// applied automatically, everything else can be picked by hand. Icons are cached on disk and keyed by folder name, so the same game folder in
/// different watch roots shares one icon.
/// </summary>
public static partial class GameIconService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string CacheFolder = GameIconHttp.CacheFolder;

    /// <summary>The providers in priority order: automatic lookups try them one after another.</summary>
    public static IReadOnlyList<IGameIconProvider> Providers { get; } = new IGameIconProvider[]
    {
        new SteamIconProvider(), new SteamGridDbIconProvider(), new IgdbIconProvider()
    };

    public static IGameIconProvider GetProvider(IconProvider id) => Providers.First(p => p.Id == id);

    /// <summary>Folders that are never games (desktop recordings, tools...).</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop", "videos", "captures", "clips", "recordings", "replays", "highlights", "screen recordings",
        "nvidia", "shadowplay", "obs", "temp", "other", "misc", "new folder"
    };

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
    public static GameIconResult? PickConfident(string folderName, IReadOnlyList<GameIconResult> results)
    {
        GameIconResult? best = null;
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
    // Providers
    // ---------------------------------------------------------------------------------------------

    public static Task<List<GameIconResult>> SearchAsync(IconProvider provider, string term, CancellationToken ct = default) =>
        GetProvider(provider).SearchAsync(term, ct);

    /// <summary>
    /// Searches every configured provider at once; Steam's results come first. A provider that is down is
    /// skipped as long as another one answered.
    /// </summary>
    public static async Task<List<GameIconResult>> SearchAllAsync(string term, CancellationToken ct = default)
    {
        var active = Providers.Where(p => p.IsConfigured).ToList();
        var tasks = active.Select(async p =>
        {
            try { return (Results: await p.SearchAsync(term, ct), Error: (Exception?)null); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return (Results: new List<GameIconResult>(), Error: ex); }
        }).ToList();
        var outcomes = await Task.WhenAll(tasks);
        if (outcomes.Length > 0 && outcomes.All(o => o.Error != null)) throw outcomes[0].Error!;
        foreach (var o in outcomes.Where(o => o.Error != null)) Helpers.Log.Error("GameIcon search", o.Error!.Message);
        return outcomes.SelectMany(o => o.Results).ToList();
    }

    /// <summary>Downloads the chosen game's icon; throws when the provider has no usable image for it.</summary>
    public static async Task<string> DownloadAsync(GameIconResult game, CancellationToken ct = default) =>
        await GetProvider(game.Provider).DownloadAsync(game, ct)
        ?? throw new InvalidOperationException($"{game.Provider} has no icon for {game.Name}");

    /// <summary>
    /// Finds an icon for a folder automatically: Steam first, then SteamGridDB, then IGDB (each only when
    /// configured). A provider whose match can't be downloaded hands over to the next one. When nothing is
    /// found the result says so, and that is remembered so the stores aren't asked again for a while.
    /// Throws when nothing was found and a provider could not be reached.
    /// </summary>
    public static async Task<GameIconInfo> LookUpAsync(string folderName, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var info = new GameIconInfo { Source = GameIconSource.None, CheckedUtc = DateTime.UtcNow };
            string term = Normalize(folderName);
            Exception? unreachable = null;
            foreach (var provider in Providers.Where(p => p.IsConfigured))
            {
                try
                {
                    var results = await provider.SearchAsync(term, ct);
                    var match = PickConfident(folderName, results);
                    if (match == null) continue;
                    string? path = await provider.DownloadAsync(match, ct);
                    if (path == null) continue;
                    info.ImagePath = path;
                    info.Provider = provider.Id;
                    info.AppId = match.Id;
                    info.GameName = match.Name;
                    info.Source = GameIconSource.Steam;
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Helpers.Log.Error("GameIcon lookup", $"{provider.DisplayName}, {folderName}: {ex.Message}");
                    unreachable = ex;
                }
            }
            // Be gentle with the stores between lookups
            await Task.Delay(350, ct);
            if (info.Source == GameIconSource.None && unreachable != null) throw unreachable;
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
