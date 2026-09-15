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
    public DateTime CreatedDateUtc { get; set; }
    public DateTime ModifiedDateUtc { get; set; }
}
