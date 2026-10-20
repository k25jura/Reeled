using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

/// <summary>SteamGridDB: community-made icons for games, also those that aren't on Steam. Needs an API key.</summary>
public sealed class SteamGridDbIconProvider : IGameIconProvider
{
    private const string Api = "https://www.steamgriddb.com/api/v2";

    private sealed record Found(string Url, string? Thumb);

    /// <summary>The best icon per game (null = none), remembered so a search preview and the download share one request.</summary>
    private readonly ConcurrentDictionary<int, Found?> _icons = new();

    public IconProvider Id => IconProvider.SteamGridDb;
    public string DisplayName => "SteamGridDB";
    public bool IsConfigured => GameIconCredentials.IsConfigured(IconProvider.SteamGridDb);

    private static async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GameIconCredentials.SteamGridDbKey);
        using var response = await GameIconHttp.Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<List<GameIconResult>> SearchAsync(string term, CancellationToken ct)
    {
        var results = new List<GameIconResult>();
        if (string.IsNullOrWhiteSpace(term) || !IsConfigured) return results;
        using var doc = await GetAsync("/search/autocomplete/" + Uri.EscapeDataString(term.Trim()), ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return results;
        foreach (var item in data.EnumerateArray().Take(10))
        {
            if (!item.TryGetProperty("id", out var id) || !id.TryGetInt32(out int gameId)) continue;
            string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            results.Add(new GameIconResult(IconProvider.SteamGridDb, gameId, name, null));
        }
        return results;
    }

    private async Task<Found?> FindIconAsync(int gameId, CancellationToken ct)
    {
        if (_icons.TryGetValue(gameId, out var known)) return known;
        using var doc = await GetAsync($"/icons/game/{gameId}", ct);
        Found? best = null;
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            // Results come best-rated first; take the first picture the app can show (no .ico files)
            foreach (var icon in data.EnumerateArray())
            {
                string? mime = icon.TryGetProperty("mime", out var m) ? m.GetString() : null;
                if (mime is not ("image/png" or "image/jpeg" or "image/webp")) continue;
                if (icon.TryGetProperty("nsfw", out var nsfw) && nsfw.ValueKind == JsonValueKind.True) continue;
                string? url = icon.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(url)) continue;
                string? thumb = icon.TryGetProperty("thumb", out var t) ? t.GetString() : null;
                best = new Found(url, thumb);
                break;
            }
        }
        _icons[gameId] = best;
        return best;
    }

    public async Task<string?> ResolveThumbnailAsync(GameIconResult game, CancellationToken ct)
    {
        var icon = await FindIconAsync(game.Id, ct);
        return icon?.Thumb ?? icon?.Url;
    }

    public async Task<string?> DownloadAsync(GameIconResult game, CancellationToken ct)
    {
        Directory.CreateDirectory(GameIconHttp.CacheFolder);
        string target = Path.Combine(GameIconHttp.CacheFolder, $"sgdb-{game.Id}.png");
        if (File.Exists(target) && new FileInfo(target).Length > 0) return target;

        var icon = await FindIconAsync(game.Id, ct);
        if (icon == null) return null;
        return await GameIconHttp.DownloadFirstAsync(new[] { icon.Url, icon.Thumb }, target, ct);
    }
}
