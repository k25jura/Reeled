namespace Reeled.Models;

/// <summary>How a clip is prepared for sharing.</summary>
public enum ShareQuality
{
    /// <summary>Re-encoded with FFmpeg to fit <see cref="SharePreferences.TargetSizeMb"/>.</summary>
    Compressed,
    /// <summary>The file as it is.</summary>
    Original,
    /// <summary>Re-encoded at the user's own resolution, frame rate and bitrate.</summary>
    Custom
}

/// <summary>Where a shared clip is uploaded.</summary>
public enum ShareDestination
{
    /// <summary>litterbox.catbox.moe: anonymous, deleted after <see cref="SharePreferences.LitterboxExpiry"/>.</summary>
    Litterbox,
    /// <summary>catbox.moe: anonymous and permanent, 200 MB at most.</summary>
    Catbox,
    /// <summary>gofile.io: anonymous; a download page rather than a direct video link.</summary>
    GoFile,
    /// <summary>pixeldrain.com: needs the user's API key.</summary>
    PixelDrain,
    /// <summary>No upload: the (compressed) file is made and put on the clipboard.</summary>
    FileOnly
}

public class SharePreferences
{
    public ShareQuality Quality { get; set; } = ShareQuality.Compressed;
    /// <summary>Size the compressed file must fit in, in MB (Discord without Nitro takes 10).</summary>
    public int TargetSizeMb { get; set; } = 20;
    public ShareDestination Destination { get; set; } = ShareDestination.Litterbox;
    /// <summary>1h, 12h, 24h or 72h.</summary>
    public string LitterboxExpiry { get; set; } = "72h";
    /// <summary>Compress with the graphics card's encoder (NVENC, AMF, Quick Sync) when there is one.</summary>
    public bool UseHardwareEncoder { get; set; } = true;
    public bool CopyLinkAutomatically { get; set; } = true;
    /// <summary>ffmpeg.exe picked by the user; empty = found automatically.</summary>
    public string? FfmpegPath { get; set; }
    /// <summary>PixelDrain API key, encrypted for this Windows user (DPAPI, base64).</summary>
    public string? ProtectedPixelDrainKey { get; set; }

    /// <summary>Custom quality: output height (0 = the source's).</summary>
    public int CustomHeight { get; set; } = 1080;
    /// <summary>Custom quality: frame rate (0 = the source's).</summary>
    public int CustomFps { get; set; }
    /// <summary>Custom quality: video bitrate in kbit/s.</summary>
    public int CustomVideoKbps { get; set; } = 8000;
}

/// <summary>One share of a clip: where it went, until when, and the compressed copy it made.</summary>
public class SharedLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>The original clip.</summary>
    public string ClipPath { get; set; } = string.Empty;
    /// <summary>Null when the video was only copied, not uploaded.</summary>
    public string? Link { get; set; }
    public ShareDestination Destination { get; set; }
    public ShareQuality Quality { get; set; }
    public DateTime SharedUtc { get; set; }
    /// <summary>When the host deletes the file (Litterbox); null = kept.</summary>
    public DateTime? ExpiresUtc { get; set; }
    /// <summary>The compressed or cut copy in the Shared folder; null when the original was sent or the copy was deleted.</summary>
    public string? CopyPath { get; set; }
    public long Bytes { get; set; }
    /// <summary>Only this part of the clip was shared (milliseconds).</summary>
    public long? StartMs { get; set; }
    public long? LengthMs { get; set; }
}
