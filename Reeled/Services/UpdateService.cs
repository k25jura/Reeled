using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    private const string StagedMarker = "staged.json";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    // The package is a few hundred MB, so downloads get no overall timeout (cancellation still works)
    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public Version CurrentVersion { get; }

    private static string UpdatesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reeled", "Updates");

    private static string InstallDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public bool CanSelfUpdate =>
        File.Exists(Path.Combine(InstallDirectory, "Reeled.exe")) &&
        File.Exists(Path.Combine(InstallDirectory, "Uninstall.exe"));

    public UpdateService()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        CurrentVersion = ver != null ? new Version(ver.Major, ver.Minor, ver.Build >= 0 ? ver.Build : 0) : new Version(1, 0, 0);
    }

    private HttpRequestMessage CreateApiRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ReeledApp", CurrentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        return request;
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            IsUpdateAvailable = false
        };

        try
        {
            using var request = CreateApiRequest($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            using var response = await HttpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return await CheckReleasesListFallbackAsync(cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                info.ErrorMessage = response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "GitHub rate limit reached. Try again later."
                    : $"GitHub API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
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

    private async Task<UpdateInfo> CheckReleasesListFallbackAsync(CancellationToken cancellationToken)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            IsUpdateAvailable = false
        };

        try
        {
            using var request = CreateApiRequest($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=5");
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
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var release in doc.RootElement.EnumerateArray())
                {
                    bool isDraft = release.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;
                    bool isPre = release.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
                    if (!isDraft && !isPre) return ParseRelease(release, CurrentVersion);
                }
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

        if (element.TryGetProperty("tag_name", out var tagProp)) info.LatestVersionTag = tagProp.GetString() ?? string.Empty;
        if (element.TryGetProperty("name", out var nameProp)) info.ReleaseName = nameProp.GetString() ?? string.Empty;
        if (element.TryGetProperty("body", out var bodyProp)) info.ReleaseNotes = bodyProp.GetString() ?? string.Empty;
        if (element.TryGetProperty("html_url", out var urlProp)) info.ReleaseHtmlUrl = urlProp.GetString() ?? string.Empty;
        if (element.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var pubDate)) info.PublishedAt = pubDate;

        // "v1.0.1" -> "1.0.1"; "1.1" -> "1.1.0"
        string cleanTag = info.LatestVersionTag.TrimStart('v', 'V').Trim();
        if (Version.TryParse(cleanTag, out var parsed) || Version.TryParse(cleanTag + ".0", out parsed))
        {
            info.LatestVersion = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            info.IsUpdateAvailable = info.LatestVersion > currentVer;
        }

        // payload.zip is what the in-app updater installs; the setup exe is the fallback for copies that
        // can't replace their own files
        if (element.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
        {
            int bestInstallerRank = int.MaxValue;
            foreach (var asset in assetsProp.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                string downloadUrl = asset.TryGetProperty("browser_download_url", out var aUrl) ? aUrl.GetString() ?? "" : "";
                long size = asset.TryGetProperty("size", out var aSize) ? aSize.GetInt64() : 0;
                if (string.IsNullOrEmpty(downloadUrl)) continue;

                if (assetName.Equals("payload.zip", StringComparison.OrdinalIgnoreCase))
                {
                    info.PackageDownloadUrl = downloadUrl;
                    info.PackageSize = size;
                    continue;
                }
                if (assetName.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase))
                {
                    info.ChecksumsUrl = downloadUrl;
                    continue;
                }

                int rank = Path.GetExtension(assetName).ToLowerInvariant() switch
                {
                    ".exe" when assetName.Equals("ReeledSetup.exe", StringComparison.OrdinalIgnoreCase) => 0,
                    ".exe" when assetName.StartsWith("ReeledSetup", StringComparison.OrdinalIgnoreCase) => 1,
                    _ => int.MaxValue
                };
                if (rank < bestInstallerRank)
                {
                    bestInstallerRank = rank;
                    info.InstallerDownloadUrl = downloadUrl;
                    info.InstallerSize = size;
                }
            }
        }

        return info;
    }

    public async Task<StagedUpdate> DownloadAndStageAsync(UpdateInfo updateInfo, IProgress<DownloadProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.PackageDownloadUrl))
        {
            throw new InvalidOperationException("This release has no update package.");
        }
        var version = updateInfo.LatestVersion ?? throw new InvalidOperationException("Unknown release version.");

        string versionDir = Path.Combine(UpdatesRoot, version.ToString());
        string zipPath = Path.Combine(versionDir, "payload.zip");
        string stagedDir = Path.Combine(versionDir, "app");
        Directory.CreateDirectory(versionDir);

        await DownloadFileAsync(updateInfo.PackageDownloadUrl, zipPath, updateInfo.PackageSize, progress, cancellationToken);
        progress?.Report(new DownloadProgressReport { Percent = 100, IsPreparing = true });

        string? expectedHash = await TryGetExpectedHashAsync(updateInfo, "payload.zip", cancellationToken);
        await Task.Run(() =>
        {
            if (expectedHash != null)
            {
                using var stream = File.OpenRead(zipPath);
                string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The downloaded update is damaged (checksum mismatch). Please try again.");
                }
            }

            if (Directory.Exists(stagedDir)) Directory.Delete(stagedDir, true);
            Directory.CreateDirectory(stagedDir);
            string root = Path.GetFullPath(stagedDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    // Never let an entry escape the staging folder
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Unsafe path in update package: {entry.FullName}");
                    }
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }

            if (!File.Exists(Path.Combine(stagedDir, "Reeled.exe")))
            {
                throw new InvalidDataException("The update package doesn't contain Reeled.exe.");
            }

            File.Delete(zipPath);
            var marker = new StagedMarkerData { Version = version.ToString(), Tag = updateInfo.LatestVersionTag };
            File.WriteAllText(Path.Combine(versionDir, StagedMarker), JsonSerializer.Serialize(marker, StagedMarkerContext.Default.StagedMarkerData));
        }, cancellationToken);

        return new StagedUpdate(version, updateInfo.LatestVersionTag, stagedDir);
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfo updateInfo, IProgress<DownloadProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.InstallerDownloadUrl))
        {
            throw new InvalidOperationException("This release has no installer.");
        }
        Directory.CreateDirectory(UpdatesRoot);
        string filePath = Path.Combine(UpdatesRoot, $"ReeledSetup-{updateInfo.LatestVersionTag}.exe");
        await DownloadFileAsync(updateInfo.InstallerDownloadUrl, filePath, updateInfo.InstallerSize, progress, cancellationToken);
        return filePath;
    }

    private async Task<string?> TryGetExpectedHashAsync(UpdateInfo info, string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(info.ChecksumsUrl)) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, info.ChecksumsUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ReeledApp", CurrentVersion.ToString()));
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            // Accepts "HASH  file" lines as well as Get-FileHash table output ("SHA256  HASH  path\file")
            foreach (var rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (!line.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var part in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length == 64 && System.Text.RegularExpressions.Regex.IsMatch(part, "^[0-9A-Fa-f]{64}$"))
                    {
                        return part;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Helpers.Log.Error("Update.Checksums", ex);
        }
        return null;
    }

    private async Task DownloadFileAsync(string url, string filePath, long expectedSize, IProgress<DownloadProgressReport>? progress, CancellationToken cancellationToken)
    {
        string partialPath = filePath + ".part";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ReeledApp", CurrentVersion.ToString()));

        using var response = await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
        if (totalBytes <= 0) totalBytes = 1;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var fileStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        long totalRead = 0;
        try
        {
            byte[] buffer = new byte[81920];
            int bytesRead;
            var stopwatch = Stopwatch.StartNew();
            long lastReportedBytes = 0;
            var lastReportedTime = stopwatch.Elapsed;
            double smoothedSpeed = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                var elapsed = stopwatch.Elapsed;
                if (elapsed - lastReportedTime > TimeSpan.FromMilliseconds(250))
                {
                    double seconds = (elapsed - lastReportedTime).TotalSeconds;
                    double speed = (totalRead - lastReportedBytes) / (1024.0 * 1024.0) / seconds;
                    // Smooth the readout so it doesn't jump between values
                    smoothedSpeed = smoothedSpeed <= 0 ? speed : smoothedSpeed * 0.7 + speed * 0.3;

                    progress?.Report(new DownloadProgressReport
                    {
                        BytesDownloaded = totalRead,
                        TotalBytes = totalBytes,
                        Percent = Math.Clamp((double)totalRead / totalBytes * 100.0, 0.0, 100.0),
                        SpeedMbPerSec = smoothedSpeed
                    });

                    lastReportedBytes = totalRead;
                    lastReportedTime = elapsed;
                }
            }
        }
        catch
        {
            await fileStream.DisposeAsync();
            try { File.Delete(partialPath); } catch { }
            throw;
        }

        await fileStream.DisposeAsync();
        // Only expose a complete download under the final name
        File.Move(partialPath, filePath, overwrite: true);
    }

    public StagedUpdate? GetStagedUpdate()
    {
        try
        {
            if (!Directory.Exists(UpdatesRoot)) return null;
            StagedUpdate? best = null;
            foreach (var dir in Directory.GetDirectories(UpdatesRoot))
            {
                string marker = Path.Combine(dir, StagedMarker);
                string app = Path.Combine(dir, "app");
                if (!File.Exists(marker) || !File.Exists(Path.Combine(app, "Reeled.exe"))) continue;
                var data = JsonSerializer.Deserialize(File.ReadAllText(marker), StagedMarkerContext.Default.StagedMarkerData);
                if (data == null || !Version.TryParse(data.Version, out var v) || v <= CurrentVersion) continue;
                if (best == null || v > best.Version)
                {
                    best = new StagedUpdate(v, string.IsNullOrEmpty(data.Tag) ? $"v{v}" : data.Tag, app);
                }
            }
            return best;
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("Update.GetStaged", ex);
            return null;
        }
    }

    public void CleanupOldUpdates()
    {
        try
        {
            if (!Directory.Exists(UpdatesRoot)) return;
            foreach (var dir in Directory.GetDirectories(UpdatesRoot))
            {
                if (!Version.TryParse(Path.GetFileName(dir), out var v) || v <= CurrentVersion)
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            // Setup downloads, the helper script and half-finished files from earlier versions
            foreach (var file in Directory.GetFiles(UpdatesRoot))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("Update.Cleanup", ex);
        }
    }

    public void ApplyStagedUpdate(StagedUpdate update, bool restart)
    {
        string target = InstallDirectory;
        Directory.CreateDirectory(UpdatesRoot);
        string script = Path.Combine(UpdatesRoot, "apply-update.ps1");
        File.WriteAllText(script, ApplyScript, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        string args =
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" " +
            $"-ProcessId {Environment.ProcessId} -Source \"{update.StagedDirectory}\" -Target \"{target}\" " +
            $"-Version \"{update.Version}\" -Restart {(restart ? 1 : 0)}";

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = args,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = UpdatesRoot
        };
        // Copies installed under Program Files need elevation to replace their files
        if (!IsDirectoryWritable(target))
        {
            psi.Verb = "runas";
        }
        Process.Start(psi);
    }

    private static bool IsDirectoryWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for Reeled to exit, copies the staged files over the install folder (retrying while files are
    /// still locked), records the new version for Windows Settings > Apps and optionally starts Reeled again.
    /// </summary>
    private const string ApplyScript = """
        param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Version, [int]$Restart = 1)
        $ErrorActionPreference = 'Continue'
        $logDir = Join-Path $env:LOCALAPPDATA 'Reeled\Logs'
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
        $log = Join-Path $logDir 'update.log'
        function Write-Log([string]$m) { Add-Content -Path $log -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $m) }

        Write-Log "Applying $Version from '$Source' to '$Target'"
        try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction Stop } catch { }

        # Any other Reeled window from this folder would keep files locked
        Get-Process -Name Reeled -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and $_.Path.StartsWith($Target, [System.StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object {
            $_.CloseMainWindow() | Out-Null
            if (-not $_.WaitForExit(5000)) { $_.Kill() }
        }

        $ok = $false
        for ($i = 0; $i -lt 6 -and -not $ok; $i++) {
            robocopy "$Source" "$Target" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -lt 8) { $ok = $true } else { Write-Log "robocopy exit $LASTEXITCODE, retrying"; Start-Sleep -Seconds 2 }
        }

        if ($ok) {
            Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Reeled' -Name 'DisplayVersion' -Value $Version -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Split-Path -Parent $Source) -Recurse -Force -ErrorAction SilentlyContinue
            Write-Log "Updated to $Version"
        } else {
            Write-Log "Update failed; the previous version stays installed"
        }

        if ($Restart -eq 1) {
            $flag = if ($ok) { '--updated' } else { '--update-failed' }
            Start-Process -FilePath (Join-Path $Target 'Reeled.exe') -ArgumentList $flag
        }
        """;
}

internal sealed class StagedMarkerData
{
    public string Version { get; set; } = string.Empty;
    public string? Tag { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(StagedMarkerData))]
internal partial class StagedMarkerContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
