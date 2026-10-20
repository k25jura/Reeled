using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

/// <summary>
/// IGDB (owned by Twitch): game covers. Needs a Twitch client id and secret; the app access token
/// they buy is kept until shortly before it expires. Covers are portrait, so they show cropped to a
/// square (the icon views use uniform-to-fill).
/// </summary>
public sealed class IgdbIconProvider : IGameIconProvider
{
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static string? _token;
    private static DateTime _tokenExpiresUtc;
    private static string? _tokenFor;

    public IconProvider Id => IconProvider.Igdb;
    public string DisplayName => "IGDB";
    public bool IsConfigured => GameIconCredentials.IsConfigured(IconProvider.Igdb);

    private static string CoverUrl(string imageId, string size) => $"https://images.igdb.com/igdb/image/upload/{size}/{imageId}.jpg";

    private static async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        string? id = GameIconCredentials.IgdbClientId;
        string? secret = GameIconCredentials.IgdbClientSecret;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("IGDB isn't set up");
        string identity = id + "\n" + secret;

        await TokenGate.WaitAsync(ct);
        try
        {
            // A different id or secret (changed in Settings) needs a fresh token
            if (!forceRefresh && _token != null && _tokenFor == identity && DateTime.UtcNow < _tokenExpiresUtc - TimeSpan.FromMinutes(1)) return _token;

            string url = "https://id.twitch.tv/oauth2/token?client_id=" + Uri.EscapeDataString(id) +
                         "&client_secret=" + Uri.EscapeDataString(secret) + "&grant_type=client_credentials";
            using var response = await GameIconHttp.Client.PostAsync(url, null, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            string token = doc.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("No IGDB token");
            int seconds = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int s) ? s : 3600;
            _token = token;
            _tokenFor = identity;
            _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(seconds);
            return token;
        }
        finally
        {
            TokenGate.Release();
        }
    }

    private static async Task<JsonDocument> QueryAsync(string body, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            string token = await GetTokenAsync(forceRefresh: attempt > 0, ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games")
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain")
            };
            request.Headers.Add("Client-ID", GameIconCredentials.IgdbClientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await GameIconHttp.Client.SendAsync(request, ct);
            // The token may have been revoked early: get a new one once
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
    }

    public async Task<List<GameIconResult>> SearchAsync(string term, CancellationToken ct)
    {
        var results = new List<GameIconResult>();
        if (string.IsNullOrWhiteSpace(term) || !IsConfigured) return results;
        string escaped = term.Trim().Replace("\\", " ").Replace("\"", " ");
        using var doc = await QueryAsync($"search \"{escaped}\"; fields name,cover.image_id; where cover != null; limit 10;", ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id) || !id.TryGetInt32(out int gameId)) continue;
            if (!item.TryGetProperty("cover", out var cover) || cover.ValueKind != JsonValueKind.Object) continue;
            string? imageId = cover.TryGetProperty("image_id", out var img) ? img.GetString() : null;
            if (string.IsNullOrEmpty(imageId)) continue;
            string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            // The picture's id travels in the thumbnail url, so downloading needs no second request
            results.Add(new GameIconResult(IconProvider.Igdb, gameId, name, CoverUrl(imageId, "t_cover_small")));
        }
        return results;
    }

    public async Task<string?> DownloadAsync(GameIconResult game, CancellationToken ct)
    {
        Directory.CreateDirectory(GameIconHttp.CacheFolder);
        string target = Path.Combine(GameIconHttp.CacheFolder, $"igdb-{game.Id}.jpg");
        if (File.Exists(target) && new FileInfo(target).Length > 0) return target;

        // A larger cover than the preview, falling back to the preview itself
        string? big = game.ThumbnailUrl?.Replace("/t_cover_small/", "/t_cover_big/");
        return await GameIconHttp.DownloadFirstAsync(new[] { big, game.ThumbnailUrl }, target, ct);
    }
}
