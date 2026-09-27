namespace Reeled.Models;

public class AudioTrackInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public int Volume { get; set; } = 100;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? $"Track {Id}" : Name;
}
