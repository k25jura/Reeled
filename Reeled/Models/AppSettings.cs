using System.Collections.Generic;

namespace Reeled.Models;

public class AppSettings
{
    public List<string> WatchDirectories { get; set; } = new();
    public string? LastActiveDirectory { get; set; }
    public int Volume { get; set; } = 100;
    public bool IsMuted { get; set; } = false;
    public double PlaybackSpeed { get; set; } = 1.0;
    public double DefaultPlaybackSpeed { get; set; } = 1.0;
    public bool RememberPlaybackSpeed { get; set; } = true;
    public HashSet<string> Favorites { get; set; } = new();
    public Dictionary<string, List<ClipBookmark>> Bookmarks { get; set; } = new();
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
    public bool EnableClipCache { get; set; } = true;
    public bool EnableSkeletonLoading { get; set; } = true;
    public double SidebarWidth { get; set; } = 316;
    public string AppTheme { get; set; } = "Default";
    public bool EnableHardwareAcceleration { get; set; } = true;
}

public enum RepeatMode
{
    Off = 0,
    All = 1,
    One = 2
}
