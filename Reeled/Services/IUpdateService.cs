using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reeled.Services;

public class UpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public Version CurrentVersion { get; set; } = new(1, 0, 0);
    public Version? LatestVersion { get; set; }
    public string LatestVersionTag { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string ReleaseHtmlUrl { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>payload.zip: the app files the in-app updater installs.</summary>
    public string? PackageDownloadUrl { get; set; }
    public long PackageSize { get; set; }

    /// <summary>The setup executable, used when this copy can't update itself (e.g. a dev build).</summary>
    public string? InstallerDownloadUrl { get; set; }
    public long InstallerSize { get; set; }

    /// <summary>checksums.txt, used to verify the package when it lists it.</summary>
    public string? ChecksumsUrl { get; set; }

    public long DownloadSize => PackageDownloadUrl != null ? PackageSize : InstallerSize;
    public string AssetSizeFormatted => DownloadSize > 0
        ? $"{(DownloadSize / (1024.0 * 1024.0)):F0} MB"
        : string.Empty;
    public string? ErrorMessage { get; set; }
}

public class DownloadProgressReport
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public double Percent { get; set; }
    public double SpeedMbPerSec { get; set; }
    /// <summary>True once the download finished and the package is being verified and unpacked.</summary>
    public bool IsPreparing { get; set; }
    public string DownloadedMbFormatted => $"{(BytesDownloaded / (1024.0 * 1024.0)):F1}";
    public string TotalMbFormatted => $"{(TotalBytes / (1024.0 * 1024.0)):F1}";
    public string SpeedFormatted => $"{SpeedMbPerSec:F1}";
}

/// <summary>An update that has been downloaded and unpacked, waiting to be swapped in.</summary>
public sealed record StagedUpdate(Version Version, string Tag, string StagedDirectory);

public interface IUpdateService
{
    Version CurrentVersion { get; }

    /// <summary>
    /// True when this copy runs from an installed location it can replace (has Uninstall.exe next to it).
    /// Dev builds fall back to downloading the setup.
    /// </summary>
    bool CanSelfUpdate { get; }

    Task<UpdateInfo> CheckForUpdatesAsync(string? githubToken = null, CancellationToken cancellationToken = default);

    /// <summary>Downloads, verifies and unpacks the package into a staging folder.</summary>
    Task<StagedUpdate> DownloadAndStageAsync(UpdateInfo updateInfo, IProgress<DownloadProgressReport>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Downloads the setup executable (fallback for copies that can't update themselves).</summary>
    Task<string> DownloadInstallerAsync(UpdateInfo updateInfo, IProgress<DownloadProgressReport>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>A staged update newer than the running version, if one is waiting.</summary>
    StagedUpdate? GetStagedUpdate();

    /// <summary>
    /// Starts the background helper that waits for Reeled to exit, swaps the files in and (optionally)
    /// starts the new version. The caller must close the app right after.
    /// </summary>
    void ApplyStagedUpdate(StagedUpdate update, bool restart);

    /// <summary>Removes leftovers of updates that are already installed.</summary>
    void CleanupOldUpdates();
}
