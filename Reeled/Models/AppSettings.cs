using System;
using System.Collections.Generic;

namespace Reeled.Models;

public class AppSettings
{
    public bool HasInitializedDefaults { get; set; } = false;
    public List<string> WatchDirectories { get; set; } = new();
    public string? LastActiveDirectory { get; set; }
    public int Volume { get; set; } = 100;
    public int DefaultVolume { get; set; } = 100;
    public int MaxVolume { get; set; } = 200;
    public bool IsMuted { get; set; } = false;
    public bool StartMuted { get; set; } = false;
    public double PlaybackSpeed { get; set; } = 1.0;
    public double DefaultPlaybackSpeed { get; set; } = 1.0;
    public bool RememberPlaybackSpeed { get; set; } = true;
    public HashSet<string> Favorites { get; set; } = new();
    public Dictionary<string, List<ClipBookmark>> Bookmarks { get; set; } = new();
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
    public RepeatMode DefaultRepeatMode { get; set; } = RepeatMode.Off;
    public bool EnableClipCache { get; set; } = true;
    public bool EnableSkeletonLoading { get; set; } = true;
    public double SidebarWidth { get; set; } = 316;
    /// <summary>Last normal (not maximized) window bounds in physical pixels; 0 width = never saved.</summary>
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public string AppTheme { get; set; } = "Default";
    /// <summary>"System" follows the Windows accent; otherwise a #RRGGBB colour (preset or custom).</summary>
    public string AccentColor { get; set; } = "System";
    /// <summary>Colours picked with the custom picker, newest first, so they can be reused.</summary>
    public List<string> RecentAccentColors { get; set; } = new();
    /// <summary>Interface zoom (Ctrl + / Ctrl - / Ctrl 0), 1.0 = 100%.</summary>
    public double UiZoom { get; set; } = 1.0;
    /// <summary>The window's close button returns to the library while a clip is playing.</summary>
    public bool CloseButtonReturnsToLibrary { get; set; } = false;
    public string Language { get; set; } = "System";
    public bool AutoCheckUpdates { get; set; } = true;
    public int AutoHideControlsSeconds { get; set; } = 2;
    public bool AutoHideCursor { get; set; } = true;
    public bool EnableOsdNotifications { get; set; } = true;
    public bool ShowMomentsBadges { get; set; } = true;
    public bool EnableBackdropBlur { get; set; } = true;
    /// <summary>0 = Light, 1 = Balanced, 2 = Strong.</summary>
    public int BackdropBlurLevel { get; set; } = 1;
    public Dictionary<string, string> CustomClipTitles { get; set; } = new();
    public DateGroupingMode DateGrouping { get; set; } = DateGroupingMode.None;
    public ViewDensityMode ViewDensity { get; set; } = ViewDensityMode.Comfortable;
    public bool MetadataHoverOnly { get; set; } = false;
    public int SortIndex { get; set; } = 0;
    public bool ReduceMotion { get; set; } = false;
    public string? GitHubToken { get; set; }
    public DateTime? LastUpdateCheckUtc { get; set; }
    /// <summary>Download updates in the background; they install when Reeled closes.</summary>
    public bool AutoDownloadUpdates { get; set; } = true;
    /// <summary>Release tag whose "update available" prompt was already shown.</summary>
    public string? DismissedUpdateVersion { get; set; }
    /// <summary>Version that ran last, to greet the user after an update.</summary>
    public string? LastRunVersion { get; set; }
    public bool ResumePlayback { get; set; } = true;
    /// <summary>Seconds the skip buttons and arrow keys jump.</summary>
    public int SkipSeconds { get; set; } = 5;
    public WheelAction PlayerWheelAction { get; set; } = WheelAction.Volume;
    /// <summary>GPU video decoding; turning it off helps with files that show artifacts or green frames.</summary>
    public bool HardwareDecoding { get; set; } = true;
    /// <summary>Last playback position (ms) per clip path, for resuming long clips.</summary>
    public Dictionary<string, long> ResumePositions { get; set; } = new();
}

public enum WheelAction
{
    Volume = 0,
    Seek = 1,
    None = 2
}

public enum RepeatMode
{
    Off = 0,
    All = 1,
    One = 2
}

public enum DateGroupingMode
{
    None = 0,
    Day = 1,
    Month = 2,
    Year = 3
}

public enum ViewDensityMode
{
    Comfortable = 0,
    Compact = 1,
    Large = 2
}
