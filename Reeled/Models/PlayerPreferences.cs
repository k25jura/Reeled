using System;
using System.Collections.Generic;

namespace Reeled.Models;

/// <summary>Picture adjustments (libvlc "adjust" filter). Neutral values leave the filter off.</summary>
public class VideoAdjustments
{
    public double Brightness { get; set; } = 1.0;
    public double Contrast { get; set; } = 1.0;
    public double Saturation { get; set; } = 1.0;
    public double Gamma { get; set; } = 1.0;
    public double Hue { get; set; } = 0.0;

    public bool IsDefault =>
        Math.Abs(Brightness - 1) < 0.005 && Math.Abs(Contrast - 1) < 0.005 && Math.Abs(Saturation - 1) < 0.005 &&
        Math.Abs(Gamma - 1) < 0.005 && Math.Abs(Hue) < 0.5;

    public VideoAdjustments Clone() => (VideoAdjustments)MemberwiseClone();
}

public enum SubtitleBackground
{
    Outline = 0,
    Box = 1,
    None = 2
}

public class SubtitleStyle
{
    /// <summary>1.0 = libvlc's default size.</summary>
    public double Scale { get; set; } = 1.0;
    /// <summary>0xRRGGBB.</summary>
    public int ColorRgb { get; set; } = 0xFFFFFF;
    public SubtitleBackground Background { get; set; } = SubtitleBackground.Outline;
    public bool Bold { get; set; }

    public bool IsDefault => Math.Abs(Scale - 1) < 0.01 && ColorRgb == 0xFFFFFF && Background == SubtitleBackground.Outline && !Bold;

    public SubtitleStyle Clone() => (SubtitleStyle)MemberwiseClone();
}

public enum ToolbarLayoutMode
{
    /// <summary>Seek bar above the buttons.</summary>
    Classic = 0,
    /// <summary>One row: buttons, seek bar, buttons.</summary>
    Compact = 1
}

/// <summary>Which player buttons are shown, where, and in what order.</summary>
public class PlayerToolbarLayout
{
    public ToolbarLayoutMode Mode { get; set; } = ToolbarLayoutMode.Classic;
    public List<string> Left { get; set; } = new() { "moments", "repeat" };
    public List<string> Center { get; set; } = new() { "prev", "play", "next" };
    public List<string> Right { get; set; } = new() { "tracks", "queue", "volume", "fullscreen" };

    public static PlayerToolbarLayout Default => new();

    public PlayerToolbarLayout Clone() => new()
    {
        Mode = Mode,
        Left = new List<string>(Left),
        Center = new List<string>(Center),
        Right = new List<string>(Right)
    };
}

public enum OsdPosition
{
    Top = 0,
    Center = 1,
    Bottom = 2
}

/// <summary>On-screen display look.</summary>
public class OsdPreferences
{
    public OsdPosition Position { get; set; } = OsdPosition.Top;
    /// <summary>Main text size in DIPs.</summary>
    public double FontSize { get; set; } = 52;
    /// <summary>Tinted gradient behind the text.</summary>
    public bool ShowBackdrop { get; set; } = true;
    /// <summary>Tint the backdrop by action (volume, seek, speed...); otherwise neutral.</summary>
    public bool TintByAction { get; set; } = true;
    /// <summary>How long a message stays, in milliseconds.</summary>
    public int DurationMs { get; set; } = 1100;
    /// <summary>Second line with details, e.g. "01:20 → 01:25" after a skip.</summary>
    public bool ShowDetails { get; set; } = true;
}

/// <summary>Session settings that can be remembered across clips.</summary>
public class PlayerSessionPreferences
{
    public bool RememberVideo { get; set; }
    public VideoAdjustments Video { get; set; } = new();
    public string? AspectRatio { get; set; }

    public bool RememberAudio { get; set; }
    public bool EqualizerEnabled { get; set; }
    public int EqualizerPreset { get; set; } = -1;
    public float[] EqualizerBands { get; set; } = new float[10];
    public float EqualizerPreamp { get; set; }
    public long AudioDelayMs { get; set; }
    public string? AudioOutputDevice { get; set; }

    public SubtitleStyle Subtitles { get; set; } = new();
}
