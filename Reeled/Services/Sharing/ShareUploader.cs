using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services.Sharing;

/// <summary>An upload failed for a reason worth telling the user as is (too big, no key, host down).</summary>
public sealed class ShareUploadException : Exception
{
    public ShareUploadException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Uploads a file to the chosen host and returns the link. All anonymous except PixelDrain, which stopped
/// taking anonymous uploads and needs the user's API key.
/// </summary>
public static class ShareUploader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var version = typeof(ShareUploader).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Reeled/{version} (+https://github.com/k25jura/Reeled)");
        return client;
    }

    /// <summary>The largest file a host accepts, or null when it has no practical limit.</summary>
    public static long? MaxBytes(ShareDestination destination) => destination switch
    {
        ShareDestination.Catbox => 200L * 1024 * 1024,
        ShareDestination.Litterbox => 1024L * 1024 * 1024,
        _ => null
    };

    /// <summary>Whether the host gives a link that plays right in Discord and browsers (not a download page).</summary>
    public static bool GivesDirectLink(ShareDestination destination) =>
        destination is ShareDestination.Catbox or ShareDestination.Litterbox or ShareDestination.PixelDrain;

    public static async Task<string> UploadAsync(ShareDestination destination, string path, string litterboxExpiry,
        string? pixelDrainKey, IProgress<double> progress, CancellationToken token)
    {
        long size = new FileInfo(path).Length;
        if (MaxBytes(destination) is long max && size > max)
        {
            throw new ShareUploadException(string.Format(Loc["Share_Error_TooBig"], HostName(destination), max / (1024 * 1024)));
        }

        try
        {
            return destination switch
            {
                ShareDestination.Catbox => await CatboxAsync("https://catbox.moe/user/api.php", path, null, progress, token),
                ShareDestination.Litterbox => await CatboxAsync("https://litterbox.catbox.moe/resources/internals/api.php", path, litterboxExpiry, progress, token),
                ShareDestination.GoFile => await GoFileAsync(path, progress, token),
                ShareDestination.PixelDrain => await PixelDrainAsync(path, pixelDrainKey, progress, token),
                _ => throw new ArgumentOutOfRangeException(nameof(destination))
            };
        }
        catch (HttpRequestException ex)
        {
            throw new ShareUploadException(string.Format(Loc["Share_Error_Network"], HostName(destination)), ex);
        }
    }

    public static string HostName(ShareDestination destination) => destination switch
    {
        ShareDestination.Catbox => "Catbox",
        ShareDestination.Litterbox => "Litterbox",
        ShareDestination.GoFile => "GoFile",
        ShareDestination.PixelDrain => "PixelDrain",
        _ => string.Empty
    };

    private static ILocalizationService Loc => App.GetService<ILocalizationService>();

    /// <summary>Catbox and Litterbox share one API: a multipart form, the link as plain text.</summary>
    private static async Task<string> CatboxAsync(string endpoint, string path, string? expiry, IProgress<double> progress, CancellationToken token)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("fileupload"), "reqtype");
        if (expiry != null) form.Add(new StringContent(expiry), "time");
        using var file = OpenFile(path, progress);
        form.Add(file, "fileToUpload", Path.GetFileName(path));

        using var response = await Http.PostAsync(endpoint, form, token);
        string body = (await response.Content.ReadAsStringAsync(token)).Trim();
        if (!response.IsSuccessStatusCode || !body.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ShareUploadException(string.Format(Loc["Share_Error_Host"], endpoint.Contains("litterbox") ? "Litterbox" : "Catbox",
                string.IsNullOrEmpty(body) ? ((int)response.StatusCode).ToString() : Shorten(body)));
        }
        return body;
    }

    /// <summary>GoFile guest upload: <c>upload.gofile.io</c> picks the nearest server; the answer is a download page.</summary>
    private static async Task<string> GoFileAsync(string path, IProgress<double> progress, CancellationToken token)
    {
        using var form = new MultipartFormDataContent();
        using var file = OpenFile(path, progress);
        form.Add(file, "file", Path.GetFileName(path));

        using var response = await Http.PostAsync("https://upload.gofile.io/uploadfile", form, token);
        string body = await response.Content.ReadAsStringAsync(token);
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("status", out var status) && status.GetString() == "ok" &&
                root.TryGetProperty("data", out var data) && data.TryGetProperty("downloadPage", out var page) &&
                page.GetString() is string link)
            {
                return link;
            }
            throw new ShareUploadException(string.Format(Loc["Share_Error_Host"], "GoFile",
                root.TryGetProperty("status", out var s) ? s.ToString() : ((int)response.StatusCode).ToString()));
        }
        catch (JsonException)
        {
            throw new ShareUploadException(string.Format(Loc["Share_Error_Host"], "GoFile", ((int)response.StatusCode).ToString()));
        }
    }

    /// <summary>PixelDrain: PUT the raw file with the key as the Basic-auth password; the viewer page is /u/{id}.</summary>
    private static async Task<string> PixelDrainAsync(string path, string? key, IProgress<double> progress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ShareUploadException(Loc["Share_Error_PixelDrainKey"]);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"https://pixeldrain.com/api/file/{Uri.EscapeDataString(Path.GetFileName(path))}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(":" + key.Trim())));
        using var file = OpenFile(path, progress);
        request.Content = file;

        using var response = await Http.SendAsync(request, token);
        string body = await response.Content.ReadAsStringAsync(token);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new ShareUploadException(Loc["Share_Error_PixelDrainKey"]);
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (response.IsSuccessStatusCode && root.TryGetProperty("id", out var id) && id.GetString() is string fileId)
            {
                return $"https://pixeldrain.com/u/{fileId}";
            }
            string reason = root.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty
                : root.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : ((int)response.StatusCode).ToString();
            throw new ShareUploadException(string.Format(Loc["Share_Error_Host"], "PixelDrain", reason));
        }
        catch (JsonException)
        {
            throw new ShareUploadException(string.Format(Loc["Share_Error_Host"], "PixelDrain", ((int)response.StatusCode).ToString()));
        }
    }

    private static ProgressFileContent OpenFile(string path, IProgress<double> progress)
    {
        var content = new ProgressFileContent(path, progress);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        return content;
    }

    private static string Shorten(string text) => text.Length > 120 ? text[..120] + "…" : text;

    /// <summary>Streams a file in 80 KB chunks and reports how much has gone out.</summary>
    private sealed class ProgressFileContent : HttpContent
    {
        private readonly string _path;
        private readonly IProgress<double> _progress;
        private readonly long _length;

        public ProgressFileContent(string path, IProgress<double> progress)
        {
            _path = path;
            _progress = progress;
            _length = new FileInfo(path).Length;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            var buffer = new byte[81920];
            long sent = 0;
            await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, useAsync: true);
            int read;
            while ((read = await file.ReadAsync(buffer, token)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), token);
                sent += read;
                _progress.Report(_length > 0 ? (double)sent / _length : 1);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
