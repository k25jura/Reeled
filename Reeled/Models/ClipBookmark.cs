using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Reeled.Models;

public partial class ClipBookmark : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TimeSpan Timestamp { get; set; }
    public double PositionPercentage { get; set; }

    public TimeSpan? EndTimestamp { get; set; }
    public double? EndPositionPercentage { get; set; }

    [ObservableProperty]
    private string _colorHex = "#FFD700";

    [ObservableProperty]
    private string _label = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRange => EndTimestamp.HasValue && EndTimestamp.Value > Timestamp;

    public string FormattedTimestamp =>
        Timestamp.Hours > 0
            ? Timestamp.ToString(@"hh\:mm\:ss")
            : Timestamp.ToString(@"mm\:ss");

    public string FormattedEndTimestamp =>
        EndTimestamp.HasValue
            ? (EndTimestamp.Value.Hours > 0 ? EndTimestamp.Value.ToString(@"hh\:mm\:ss") : EndTimestamp.Value.ToString(@"mm\:ss"))
            : string.Empty;

    public string FormattedRange =>
        IsRange ? $"{FormattedTimestamp} - {FormattedEndTimestamp}" : FormattedTimestamp;

    [System.Text.Json.Serialization.JsonIgnore]
    public string JumpTooltip => App.GetService<Services.ILocalizationService>()?["Player_JumpToMoment"] ?? "Jump to this moment";

    [System.Text.Json.Serialization.JsonIgnore]
    public string RenameTooltip => App.GetService<Services.ILocalizationService>()?["Player_RenameMoment"] ?? "Edit moment";

    [System.Text.Json.Serialization.JsonIgnore]
    public string RemoveTooltip => App.GetService<Services.ILocalizationService>()?["Player_RemoveMoment"] ?? "Remove moment";

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(JumpTooltip));
        OnPropertyChanged(nameof(RenameTooltip));
        OnPropertyChanged(nameof(RemoveTooltip));
    }
}
