using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Reeled.Models;

public partial class ClipBookmark : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TimeSpan Timestamp { get; set; }
    public double PositionPercentage { get; set; }

    [ObservableProperty]
    private string _label = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string FormattedTimestamp =>
        Timestamp.Hours > 0
            ? Timestamp.ToString(@"hh\:mm\:ss")
            : Timestamp.ToString(@"mm\:ss");
}

