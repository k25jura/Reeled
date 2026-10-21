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
    private bool _isExpanded = false;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _hasSubDirectories;

    /// <summary>Game icon (Steam or user-picked); the folder glyph shows while it's null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameIcon))]
    [NotifyPropertyChangedFor(nameof(FolderGlyphVisibility))]
    private Microsoft.UI.Xaml.Media.ImageSource? _gameIcon;

    public bool HasGameIcon => GameIcon != null;

    public Microsoft.UI.Xaml.Visibility FolderGlyphVisibility =>
        GameIcon == null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public ObservableCollection<DirectoryNode> SubDirectories { get; set; } = new();

    public string DisplayName =>
        !string.IsNullOrEmpty(Name) ? Name : System.IO.Path.GetFileName(FullPath);

    public string IconGlyph => IsExpanded ? "\uE838" : "\uED25";
}
