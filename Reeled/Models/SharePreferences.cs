namespace Reeled.Models;

/// <summary>How a clip is prepared for sharing.</summary>
public enum ShareQuality
{
    /// <summary>Re-encoded with FFmpeg to fit <see cref="SharePreferences.TargetSizeMb"/>.</summary>
    Compressed,
    /// <summary>The file as it is.</summary>
    Original
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
}
