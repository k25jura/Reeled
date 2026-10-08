using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Reeled.Installer.Services;

public record InstallProgress(double Percentage, string StatusText);

public class InstallService
{
    private readonly SystemIntegrationService _systemIntegration = new();

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
        await Task.Run(() =>
        {
            var loc = LocalizationService.Instance;
            progress.Report(new InstallProgress(0, loc["Installing_Status_Preparing"]));

            Directory.CreateDirectory(destinationDir);

            // Locate Payload
            using Stream? payloadStream = GetPayloadStream();
            if (payloadStream == null)
            {
                throw new InvalidOperationException("Installation payload was not found. Please ensure payload.zip is bundled with the installer.");
            }

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

                double pct = (double)current / totalEntries * 80.0; // 0% to 80% for extraction
                string statusMsg = loc.Format("Installing_Status_Extracting", current, totalEntries);
                progress.Report(new InstallProgress(pct, statusMsg));
            }

            // Copy installer as Uninstaller
            progress.Report(new InstallProgress(85, loc["Installing_Status_Shortcuts"]));
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
            progress.Report(new InstallProgress(92, loc["Installing_Status_Registering"]));
            _systemIntegration.RegisterWindowsIntegration(destinationDir, registerVideoPlayer);

            // Add/Remove Programs Registration
            _systemIntegration.RegisterUninstaller(destinationDir, totalBytesExtracted);

            progress.Report(new InstallProgress(100, loc["Installing_Status_Finishing"]));
        }, cancellationToken);
    }

    private Stream? GetPayloadStream()
    {
        // 1. Check Embedded Resource
        var assembly = Assembly.GetExecutingAssembly();
        string[] resourceNames = assembly.GetManifestResourceNames();
        foreach (var name in resourceNames)
        {
            if (name.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
            {
                var stream = assembly.GetManifestResourceStream(name);
                if (stream != null) return stream;
            }
        }

        // 2. Check Local File alongside installer
        string localPayload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload.zip");
        if (File.Exists(localPayload))
        {
            return File.OpenRead(localPayload);
        }

        // 3. Check Resources subfolder
        string resPayload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "payload.zip");
        if (File.Exists(resPayload))
        {
            return File.OpenRead(resPayload);
        }

        // 4. Check parent artifacts folder (development mode)
        string devPayload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "artifacts", "payload.zip");
        if (File.Exists(devPayload))
        {
            return File.OpenRead(devPayload);
        }

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
