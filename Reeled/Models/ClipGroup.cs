using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Reeled.Models;

public class ClipGroup : ObservableCollection<GameClip>
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.MinValue;
    public bool IsHeaderVisible { get; set; } = true;
    public bool ShowGroupDivider { get; set; } = false;

    public ClipGroup()
    {
    }

    public ClipGroup(string key, string title, string subtitle, IEnumerable<GameClip> clips, bool isHeaderVisible = true)
        : base(clips)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        IsHeaderVisible = isHeaderVisible;
    }
}
