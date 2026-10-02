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
    public string? AssetDownloadUrl { get; set; }
    public string? AssetName { get; set; }
    public long AssetSize { get; set; }
    public string AssetSizeFormatted => AssetSize > 0 
        ? $"{(AssetSize / (1024.0 * 1024.0)):F1} MB" 
        : string.Empty;
    public string? ErrorMessage { get; set; }
}

public class DownloadProgressReport
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public double Percent { get; set; }
    public double SpeedMbPerSec { get; set; }
    public string DownloadedMbFormatted => $"{(BytesDownloaded / (1024.0 * 1024.0)):F1}";
    public string TotalMbFormatted => $"{(TotalBytes / (1024.0 * 1024.0)):F1}";
    public string SpeedFormatted => $"{SpeedMbPerSec:F1}";
}

public interface IUpdateService
{
    Version CurrentVersion { get; }
    Task<UpdateInfo> CheckForUpdatesAsync(string? githubToken = null, CancellationToken cancellationToken = default);
    Task<string> DownloadUpdateAssetAsync(UpdateInfo updateInfo, IProgress<DownloadProgressReport>? progress = null, CancellationToken cancellationToken = default);
    Task LaunchInstallerAsync(string filePath);
}
