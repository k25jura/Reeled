using System;

namespace Reeled.Models;

public class ClipBookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TimeSpan Timestamp { get; set; }
    public double PositionPercentage { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string FormattedTimestamp =>
        Timestamp.Hours > 0
            ? Timestamp.ToString(@"hh\:mm\:ss")
            : Timestamp.ToString(@"mm\:ss");
}
