using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

/// <summary>The Steam store: square community icons, with the capsule art as fallback.</summary>
public sealed class SteamIconProvider : IGameIconProvider
{
    public IconProvider Id => IconProvider.Steam;
    public string DisplayName => "Steam";
    public bool IsConfigured => true;

    public async Task<List<GameIconResult>> SearchAsync(string term, CancellationToken ct)
    {
        var results = new List<GameIconResult>();
        if (string.IsNullOrWhiteSpace(term)) return results;
        string url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(term.Trim())}&l=english&cc=US";
        using var doc = JsonDocument.Parse(await GameIconHttp.Client.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("items", out var items)) return results;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) && type.GetString() != "app") continue;
            int id = item.GetProperty("id").GetInt32();
            string name = item.GetProperty("name").GetString() ?? string.Empty;
            string? img = item.TryGetProperty("tiny_image", out var t) ? t.GetString() : null;
            results.Add(new GameIconResult(IconProvider.Steam, id, name, img));
        }
        return results;
    }

    /// <summary>
    /// Downloads the community icon; when Steam has none (or the file is gone) falls back through the
    /// capsule and header art and finally the search result's own thumbnail.
    /// </summary>
    public async Task<string?> DownloadAsync(GameIconResult game, CancellationToken ct)
    {
        int appId = game.Id;
        Directory.CreateDirectory(GameIconHttp.CacheFolder);
        string target = Path.Combine(GameIconHttp.CacheFolder, $"{appId}.jpg");
        if (File.Exists(target) && new FileInfo(target).Length > 0) return target;

        string? communityIcon = null;
        string? smallCapsule = null;
        try
        {
            string input = $"{{\"ids\":[{{\"appid\":{appId}}}],\"context\":{{\"language\":\"english\",\"country_code\":\"US\",\"steam_realm\":1}},\"data_request\":{{\"include_assets\":true}}}}";
            string url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(input);
            using var doc = JsonDocument.Parse(await GameIconHttp.Client.GetStringAsync(url, ct));
            var item = doc.RootElement.GetProperty("response").GetProperty("store_items")[0];
            if (item.TryGetProperty("assets", out var assets))
            {
                if (assets.TryGetProperty("community_icon", out var icon) && !string.IsNullOrEmpty(icon.GetString()))
                {
                    communityIcon = $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{icon.GetString()}.jpg";
                }
                if (assets.TryGetProperty("asset_url_format", out var format) && assets.TryGetProperty("small_capsule", out var capsule))
                {
                    smallCapsule = "https://shared.cloudflare.steamstatic.com/store_item_assets/" +
                                   format.GetString()!.Replace("${FILENAME}", capsule.GetString());
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Helpers.Log.Error("GameIcon assets", ex.Message);
        }

        var candidates = new List<string?>
        {
            communityIcon,
            smallCapsule,
            $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/capsule_184x69.jpg",
            $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
            game.ThumbnailUrl,
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/capsule_sm_120.jpg"
        };
        return await GameIconHttp.DownloadFirstAsync(candidates, target, ct);
    }
}
