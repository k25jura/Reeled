using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reeled.Services.Sharing;

/// <summary>
/// Finds ffmpeg.exe (it isn't bundled: it's large and its own project) and installs it through winget when
/// the user asks. Looks in the folder picked in Settings, next to Reeled, on PATH (read fresh from the
/// registry, so an install made while Reeled runs is seen) and in winget's own install folders.
/// </summary>
public static class FfmpegLocator
{
    public const string DownloadPage = "https://ffmpeg.org/download.html#build-windows";
    private const string WingetPackage = "Gyan.FFmpeg";

    private static string? _cachedPath;
    private static string? _cachedVersion;

    /// <summary>Raised on the thread that noticed a change (an install finished, a path was picked).</summary>
    public static event Action? Changed;

    public static string? Find(bool refresh = false)
    {
        if (!refresh && _cachedPath != null && File.Exists(_cachedPath)) return _cachedPath;
        _cachedPath = Candidates().FirstOrDefault(File.Exists);
        _cachedVersion = null;
        return _cachedPath;
    }

    public static bool IsAvailable => Find() != null;

    private static IEnumerable<string> Candidates()
    {
        var chosen = App.GetService<ILocalStorageService>().CurrentSettings.Share?.FfmpegPath;
        if (!string.IsNullOrWhiteSpace(chosen)) yield return chosen;

        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "ffmpeg.exe");
        yield return Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe");
        yield return Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe");

        foreach (var dir in PathDirectories())
        {
            yield return Path.Combine(dir, "ffmpeg.exe");
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
        string packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(packages))
        {
            IEnumerable<string> found = Array.Empty<string>();
            try
            {
                found = Directory.EnumerateDirectories(packages, "*FFmpeg*")
                    .SelectMany(d => Directory.EnumerateFiles(d, "ffmpeg.exe", SearchOption.AllDirectories));
            }
            catch { }
            foreach (var f in found) yield return f;
        }
    }

    /// <summary>PATH as this process sees it plus the user and machine PATH as stored now.</summary>
    private static IEnumerable<string> PathDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
        };
        foreach (var source in sources)
        {
            if (string.IsNullOrEmpty(source)) continue;
            foreach (var raw in source.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string dir;
                try { dir = Environment.ExpandEnvironmentVariables(raw.Trim('"')); }
                catch { continue; }
                if (seen.Add(dir)) yield return dir;
            }
        }
    }

    /// <summary>"7.1.1" or similar, from <c>ffmpeg -version</c>; null when it can't be run.</summary>
    public static async Task<string?> GetVersionAsync()
    {
        var path = Find();
        if (path == null) return null;
        if (_cachedVersion != null) return _cachedVersion;
        try
        {
            var psi = new ProcessStartInfo(path, "-hide_banner -version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            string first = await process.StandardOutput.ReadLineAsync() ?? string.Empty;
            await process.WaitForExitAsync();
            // "ffmpeg version 7.1.1-full_build-www.gyan.dev Copyright ..."
            var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int i = Array.IndexOf(parts, "version");
            string version = i >= 0 && i + 1 < parts.Length ? parts[i + 1] : first;
            int dash = version.IndexOf('-');
            _cachedVersion = dash > 0 ? version[..dash] : version;
            return _cachedVersion;
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("FFmpeg.Version", ex);
            return null;
        }
    }

    /// <summary>Remembers a user-picked ffmpeg.exe (null goes back to finding it automatically).</summary>
    public static void SetChosenPath(string? path)
    {
        var storage = App.GetService<ILocalStorageService>();
        storage.CurrentSettings.Share ??= new Models.SharePreferences();
        storage.CurrentSettings.Share.FfmpegPath = string.IsNullOrWhiteSpace(path) ? null : path;
        _ = storage.SaveSettingsAsync(storage.CurrentSettings);
        Find(refresh: true);
        ClipCompressor.ForgetEncoder();
        Changed?.Invoke();
    }

    public static bool IsWingetAvailable =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe"));

    private static CancellationTokenSource? _watch;

    /// <summary>
    /// Opens PowerShell running <c>winget install Gyan.FFmpeg</c> (the user agreed first, and sees every step),
    /// then watches for ffmpeg.exe to appear for up to 15 minutes. Returns false when winget couldn't start.
    /// </summary>
    public static bool InstallWithWinget()
    {
        try
        {
            string command =
                $"winget install --id {WingetPackage} --exact --source winget --accept-package-agreements --accept-source-agreements; " +
                "Write-Host ''; Write-Host 'You can close this window and go back to Reeled.'";
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NoExit -Command \"{command}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("FFmpeg.Install", ex);
            return false;
        }

        _watch?.Cancel();
        var cts = _watch = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(2000, cts.Token); } catch { return; }
                if (Find(refresh: true) != null)
                {
                    ClipCompressor.ForgetEncoder();
                    Changed?.Invoke();
                    return;
                }
            }
        });
        return true;
    }
}
