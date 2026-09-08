using System.Collections.Generic;

namespace Reeled.Models;

public class AppSettings
{
    public List<string> WatchDirectories { get; set; } = new();
    public string? LastActiveDirectory { get; set; }
    public int Volume { get; set; } = 100;
    public bool IsMuted { get; set; } = false;
    public double PlaybackSpeed { get; set; } = 1.0;
    public HashSet<string> Favorites { get; set; } = new();
    public Dictionary<string, List<ClipBookmark>> Bookmarks { get; set; } = new();
}
