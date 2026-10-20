using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Reeled.Services;

/// <summary>The HTTP client and cache folder the icon providers share.</summary>
internal static class GameIconHttp
{
    public static readonly HttpClient Client = CreateClient();

    public static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reeled", "GameIcons");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Reeled/2.0 (+https://github.com/k25jura/Reeled)");
        return client;
    }

    /// <summary>Downloads an image; null on any HTTP failure or when the response isn't an image.</summary>
    public static async Task<byte[]?> TryGetImageAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await Client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                Helpers.Log.Error("GameIcon download", $"{(int)response.StatusCode} {url}");
                return null;
            }
            string? type = response.Content.Headers.ContentType?.MediaType;
            if (type != null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (HttpRequestException ex)
        {
            Helpers.Log.Error("GameIcon download", $"{ex.Message} {url}");
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out
            return null;
        }
    }

    /// <summary>Tries the urls in order and stores the first image that downloads; null when none do.</summary>
    public static async Task<string?> DownloadFirstAsync(IEnumerable<string?> urls, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheFolder);
        foreach (var url in urls)
        {
            if (string.IsNullOrEmpty(url)) continue;
            var bytes = await TryGetImageAsync(url, ct);
            if (bytes == null) continue;
            await File.WriteAllBytesAsync(target, bytes, ct);
            return target;
        }
        return null;
    }
}
