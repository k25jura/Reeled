using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Reeled.Models;

public partial class DirectoryNode : ObservableObject
{
    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isWatchRoot;

    [ObservableProperty]
    private int _clipCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconGlyph))]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _hasSubDirectories;

    public ObservableCollection<DirectoryNode> SubDirectories { get; set; } = new();

    public string DisplayName =>
        !string.IsNullOrEmpty(Name) ? Name : System.IO.Path.GetFileName(FullPath);

    public string IconGlyph => IsExpanded ? "\uE838" : "\uED25";
}
