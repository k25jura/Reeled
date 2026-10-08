using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Reeled.Installer.Services;

public record InstallProgress(double Percentage, string StatusText);

public class InstallService
{
    public const string DefaultPayloadUrl = "https://github.com/k25jura/Reeled/releases/latest/download/payload.zip";

    private readonly SystemIntegrationService _systemIntegration = new();
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    static InstallService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ReeledSetup/1.0 (Windows NT; x64)");
    }

    public string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Reeled");

    public long GetDriveFreeSpaceBytes(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "C:\\";
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return 10L * 1024 * 1024 * 1024; // Fallback 10GB
        }
    }

    public async Task InstallAsync(
        string destinationDir,
        bool createDesktopShortcut,
        bool createStartMenuShortcut,
        bool registerVideoPlayer,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var loc = LocalizationService.Instance;
        string? tempDownloadedFile = null;

        try
        {
            progress.Report(new InstallProgress(0, loc["Installing_Status_Preparing"]));
            Directory.CreateDirectory(destinationDir);

            // 1. Check if payload exists locally first
            string? localPayloadPath = GetLocalPayloadPath();
            Stream? payloadStream = null;

            if (!string.IsNullOrEmpty(localPayloadPath) && File.Exists(localPayloadPath))
            {
                payloadStream = File.OpenRead(localPayloadPath);
            }
            else
            {
                // 2. Download from GitHub Releases
                progress.Report(new InstallProgress(2, loc["Installing_Status_Connecting"]));

                tempDownloadedFile = Path.Combine(Path.GetTempPath(), $"Reeled_payload_{Guid.NewGuid():N}.zip");
                await DownloadPayloadAsync(DefaultPayloadUrl, tempDownloadedFile, progress, loc, cancellationToken);

                payloadStream = File.OpenRead(tempDownloadedFile);
            }

            using (payloadStream)
            {
                using var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
                int totalEntries = archive.Entries.Count;
                int current = 0;
                long totalBytesExtracted = 0;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fullPath = Path.Combine(destinationDir, entry.FullName);

                    // Handle directory entries
                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(fullPath);
                        current++;
                        continue;
                    }

                    string? parent = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    entry.ExtractToFile(fullPath, overwrite: true);
                    totalBytesExtracted += entry.Length;
                    current++;

                    // Extraction spans 50% to 85%
                    double pct = 50.0 + ((double)current / totalEntries * 35.0);
                    string statusMsg = loc.Format("Installing_Status_Extracting", current, totalEntries);
                    progress.Report(new InstallProgress(pct, statusMsg));
                }

                // Copy installer as Uninstaller
                progress.Report(new InstallProgress(88, loc["Installing_Status_Shortcuts"]));
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                string uninstallTarget = Path.Combine(destinationDir, "Uninstall.exe");
                if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                {
                    try
                    {
                        File.Copy(currentExe, uninstallTarget, overwrite: true);
                    }
                    catch { }
                }

                // Create Shortcuts
                string targetAppExe = Path.Combine(destinationDir, "Reeled.exe");
                _systemIntegration.CreateShortcuts(targetAppExe, createDesktopShortcut, createStartMenuShortcut);

                // Windows Capabilities & Context Menu Registration
                progress.Report(new InstallProgress(94, loc["Installing_Status_Registering"]));
                _systemIntegration.RegisterWindowsIntegration(destinationDir, registerVideoPlayer);

                // Add/Remove Programs Registration
                _systemIntegration.RegisterUninstaller(destinationDir, totalBytesExtracted);

                progress.Report(new InstallProgress(100, loc["Installing_Status_Finishing"]));
            }
        }
        finally
        {
            // Clean up temporary downloaded payload
            if (!string.IsNullOrEmpty(tempDownloadedFile) && File.Exists(tempDownloadedFile))
            {
                try { File.Delete(tempDownloadedFile); } catch { }
            }
        }
    }

    private async Task DownloadPayloadAsync(
        string url,
        string destinationFilePath,
        IProgress<InstallProgress> progress,
        LocalizationService loc,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        double totalMb = (totalBytes ?? (148L * 1024 * 1024)) / (1024.0 * 1024.0);

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        var stopwatch = Stopwatch.StartNew();
        long lastSpeedCalcTicks = 0;
        long lastSpeedBytes = 0;
        double currentSpeedMbSec = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalRead += bytesRead;

            long elapsedTicks = stopwatch.ElapsedMilliseconds;
            if (elapsedTicks - lastSpeedCalcTicks > 400)
            {
                double seconds = (elapsedTicks - lastSpeedCalcTicks) / 1000.0;
                if (seconds > 0)
                {
                    currentSpeedMbSec = ((totalRead - lastSpeedBytes) / (1024.0 * 1024.0)) / seconds;
                }
                lastSpeedCalcTicks = elapsedTicks;
                lastSpeedBytes = totalRead;
            }

            double downloadedMb = totalRead / (1024.0 * 1024.0);
            double downloadFraction = totalBytes.HasValue && totalBytes.Value > 0
                ? (double)totalRead / totalBytes.Value
                : Math.Min(downloadedMb / 150.0, 0.95);

            // Download spans 2% to 50%
            double pct = 2.0 + (downloadFraction * 48.0);
            string statusMsg = loc.Format("Installing_Status_DownloadingWithSpeed", downloadedMb, totalMb, currentSpeedMbSec);
            progress.Report(new InstallProgress(pct, statusMsg));
        }
    }

    private string? GetLocalPayloadPath()
    {
        // 1. Same directory as ReeledSetup.exe
        string localPayload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload.zip");
        if (File.Exists(localPayload)) return localPayload;

        // 2. Resources subfolder
        string resPayload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "payload.zip");
        if (File.Exists(resPayload)) return resPayload;

        // 3. Parent artifacts folder (for dev / local builds)
        string devPayload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "artifacts", "payload.zip");
        if (File.Exists(devPayload)) return devPayload;

        return null;
    }

    public async Task UninstallAsync(string installDir, bool wipeUserData, IProgress<InstallProgress> progress)
    {
        await Task.Run(() =>
        {
            var loc = LocalizationService.Instance;
            progress.Report(new InstallProgress(10, loc["Uninstall_Status_Preparing"]));

            // 1. Remove Shortcuts
            progress.Report(new InstallProgress(30, loc["Uninstall_Status_Shortcuts"]));
            _systemIntegration.RemoveShortcuts();

            // 2. Remove Registry entries
            progress.Report(new InstallProgress(50, loc["Uninstall_Status_Registry"]));
            _systemIntegration.RemoveWindowsIntegration();

            // 3. Remove App Data if requested
            if (wipeUserData)
            {
                try
                {
                    string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reeled");
                    if (Directory.Exists(localAppData))
                    {
                        Directory.Delete(localAppData, recursive: true);
                    }
                }
                catch { }
            }

            // 4. Remove Files in installDir (except Uninstall.exe which is currently running)
            progress.Report(new InstallProgress(70, loc["Uninstall_Status_Removing"]));
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

            try
            {
                if (Directory.Exists(installDir))
                {
                    foreach (var file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                    {
                        if (!string.Equals(file, currentExe, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }

                    foreach (var dir in Directory.GetDirectories(installDir, "*", SearchOption.AllDirectories))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch { }
                    }
                }
            }
            catch { }

            // 5. Schedule self-deletion via cmd
            progress.Report(new InstallProgress(100, loc["Uninstall_Status_Done"]));

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 2 /nobreak > NUL & rmdir /s /q \"{installDir}\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            catch { }
        });
    }
}
