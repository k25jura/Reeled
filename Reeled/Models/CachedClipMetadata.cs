using System;

namespace Reeled.Models;

public class CachedClipMetadata
{
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public long DurationTicks { get; set; }
    public uint VideoWidth { get; set; }
    public uint VideoHeight { get; set; }
    /// <summary>Frames per second; 0 = not probed yet (older cache entries), negative = probed, none reported.</summary>
    public double Framerate { get; set; }
    public DateTime CreatedDateUtc { get; set; }
    public DateTime ModifiedDateUtc { get; set; }
}
