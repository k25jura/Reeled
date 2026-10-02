using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reeled.Services;

public class UpdateService : IUpdateService
{
    private const string RepoOwner = "k25jura";
    private const string RepoName = "Reeled";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public Version CurrentVersion { get; }

    public UpdateService()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        CurrentVersion = ver != null ? new Version(ver.Major, ver.Minor, ver.Build >= 0 ? ver.Build : 0) : new Version(1, 0, 0);
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(string? githubToken = null, CancellationToken cancellationToken = default)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            IsUpdateAvailable = false
        };

        try
        {
            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ReeledApp", CurrentVersion.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            if (!string.IsNullOrWhiteSpace(githubToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken.Trim());
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Try listing all releases in case the latest tag is marked pre-release
                return await CheckReleasesListFallbackAsync(githubToken, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                info.ErrorMessage = $"GitHub API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
                return info;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return ParseRelease(doc.RootElement, CurrentVersion);
        }
        catch (OperationCanceledException)
        {
            info.ErrorMessage = "Check cancelled.";
            return info;
        }
        catch (Exception ex)
        {
            info.ErrorMessage = ex.Message;
            return info;
        }
    }

    private async Task<UpdateInfo> CheckReleasesListFallbackAsync(string? githubToken, CancellationToken cancellationToken)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            IsUpdateAvailable = false
        };

        try
        {
            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=5";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ReeledApp", CurrentVersion.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            if (!string.IsNullOrWhiteSpace(githubToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken.Trim());
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                info.ErrorMessage = response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "Repository or releases not found on GitHub."
                    : $"GitHub API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
                return info;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                return ParseRelease(doc.RootElement[0], CurrentVersion);
            }

            info.ErrorMessage = "No releases found on GitHub.";
            return info;
        }
        catch (Exception ex)
        {
            info.ErrorMessage = ex.Message;
            return info;
        }
    }

    private static UpdateInfo ParseRelease(JsonElement element, Version currentVer)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = currentVer
        };

        if (element.TryGetProperty("tag_name", out var tagProp))
        {
            info.LatestVersionTag = tagProp.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty("name", out var nameProp))
        {
            info.ReleaseName = nameProp.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty("body", out var bodyProp))
        {
            info.ReleaseNotes = bodyProp.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty("html_url", out var urlProp))
        {
            info.ReleaseHtmlUrl = urlProp.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var pubDate))
        {
            info.PublishedAt = pubDate;
        }

        // Clean version tag: "v1.0.1" -> "1.0.1"
        string cleanTag = info.LatestVersionTag.TrimStart('v', 'V').Trim();
        if (Version.TryParse(cleanTag, out var parsedVer))
        {
            info.LatestVersion = parsedVer;
            info.IsUpdateAvailable = parsedVer > currentVer;
        }
        else
        {
            // If tag format has fewer components e.g. "1.1", append ".0"
            if (Version.TryParse(cleanTag + ".0", out var parsedVer2))
            {
                info.LatestVersion = parsedVer2;
                info.IsUpdateAvailable = parsedVer2 > currentVer;
            }
            else
            {
                info.IsUpdateAvailable = !string.IsNullOrWhiteSpace(cleanTag) &&
                                         !string.Equals(cleanTag, currentVer.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        // Look for release assets
        if (element.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsProp.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                string downloadUrl = asset.TryGetProperty("browser_download_url", out var aUrl) ? aUrl.GetString() ?? "" : "";
                long size = asset.TryGetProperty("size", out var aSize) ? aSize.GetInt64() : 0;

                string ext = Path.GetExtension(assetName).ToLowerInvariant();
                if (ext is ".msix" or ".msixbundle" or ".exe" or ".zip")
                {
                    info.AssetName = assetName;
                    info.AssetDownloadUrl = downloadUrl;
                    info.AssetSize = size;
                    break;
                }
            }
        }

        return info;
    }

    public async Task<string> DownloadUpdateAssetAsync(UpdateInfo updateInfo, IProgress<DownloadProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.AssetDownloadUrl))
        {
            throw new InvalidOperationException("No download asset URL provided.");
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string downloadDir = Path.Combine(localAppData, "Reeled", "Updates");
        Directory.CreateDirectory(downloadDir);

        string fileName = !string.IsNullOrWhiteSpace(updateInfo.AssetName)
            ? updateInfo.AssetName
            : $"Reeled-{updateInfo.LatestVersionTag}.zip";

        string filePath = Path.Combine(downloadDir, fileName);

        using var request = new HttpRequestMessage(HttpMethod.Get, updateInfo.AssetDownloadUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ReeledApp", CurrentVersion.ToString()));

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? updateInfo.AssetSize;
        if (totalBytes <= 0) totalBytes = 1;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        var stopwatch = Stopwatch.StartNew();
        long lastReportedBytes = 0;
        var lastReportedTime = stopwatch.Elapsed;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalRead += bytesRead;

            var elapsed = stopwatch.Elapsed;
            if (elapsed - lastReportedTime > TimeSpan.FromMilliseconds(100))
            {
                double timeDeltaSec = (elapsed - lastReportedTime).TotalSeconds;
                long bytesDelta = totalRead - lastReportedBytes;
                double speedMbSec = timeDeltaSec > 0 ? (bytesDelta / (1024.0 * 1024.0)) / timeDeltaSec : 0.0;

                progress?.Report(new DownloadProgressReport
                {
                    BytesDownloaded = totalRead,
                    TotalBytes = totalBytes,
                    Percent = Math.Clamp((double)totalRead / totalBytes * 100.0, 0.0, 100.0),
                    SpeedMbPerSec = speedMbSec
                });

                lastReportedBytes = totalRead;
                lastReportedTime = elapsed;
            }
        }

        progress?.Report(new DownloadProgressReport
        {
            BytesDownloaded = totalRead,
            TotalBytes = totalBytes,
            Percent = 100.0,
            SpeedMbPerSec = 0.0
        });

        return filePath;
    }

    public Task LaunchInstallerAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Downloaded update file was not found.", filePath);
        }

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".msix" or ".msixbundle" or ".exe")
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        else
        {
            // Open directory and highlight file in Explorer
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            });
        }

        return Task.CompletedTask;
    }
}
