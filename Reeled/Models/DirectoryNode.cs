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
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<DirectoryNode> SubDirectories { get; set; } = new();

    public string IconGlyph => IsWatchRoot ? "\uE838" : "\uED25"; // FolderHorizontal vs Folder
}
