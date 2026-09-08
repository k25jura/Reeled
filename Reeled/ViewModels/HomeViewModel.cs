using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Microsoft.UI.Dispatching;
using Reeled.Models;
using Reeled.Services;

namespace Reeled.ViewModels;

public enum NavigationSection
{
    Home,
    Favorites,
    SavedMoments,
    Folder
}

public partial class HomeViewModel : ObservableObject
{
    private readonly IClipIndexerService _indexerService;
    private readonly ILocalStorageService _storageService;
    private readonly INavigationService _navigationService;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<DirectoryNode> Directories { get; } = new();
    public ObservableCollection<GameClip> AllClips { get; } = new();
    public ObservableCollection<GameClip> Clips { get; } = new();
    public ObservableCollection<GameClip> FilteredClips { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected))]
    [NotifyPropertyChangedFor(nameof(IsFavoritesSelected))]
    [NotifyPropertyChangedFor(nameof(IsSavedMomentsSelected))]
    [NotifyPropertyChangedFor(nameof(IsFolderSelected))]
    [NotifyPropertyChangedFor(nameof(CurrentDirectoryTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentDirectoryPath))]
    [NotifyPropertyChangedFor(nameof(CanOpenDirectoryInExplorer))]
    [NotifyPropertyChangedFor(nameof(EmptyStateGlyph))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateSubtitle))]
    private NavigationSection _currentSection = NavigationSection.Home;

    public bool IsHomeSelected => CurrentSection == NavigationSection.Home;
    public bool IsFavoritesSelected => CurrentSection == NavigationSection.Favorites;
    public bool IsSavedMomentsSelected => CurrentSection == NavigationSection.SavedMoments;
    public bool IsFolderSelected => CurrentSection == NavigationSection.Folder;

    public int AllClipsCount => AllClips.Count;
    public int FavoritesCount => AllClips.Count(c => c.IsFavorite);
    public int SavedMomentsCount => AllClips.Count(c => c.Bookmarks.Count > 0);

    public bool CanOpenDirectoryInExplorer =>
        CurrentSection == NavigationSection.Folder && !string.IsNullOrEmpty(SelectedDirectory?.FullPath);

    [ObservableProperty]
    private DirectoryNode? _selectedDirectory;

    [ObservableProperty]
    private GameClip? _selectedClip;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _sortIndex = 0; // 0: Newest, 1: Oldest, 2: Name, 3: Duration, 4: Size

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasDirectories => Directories.Count > 0;
    public bool HasClips => FilteredClips.Count > 0;

    public string CurrentDirectoryTitle => CurrentSection switch
    {
        NavigationSection.Home => "Home",
        NavigationSection.Favorites => "Favorites",
        NavigationSection.SavedMoments => "Saved Moments",
        NavigationSection.Folder => SelectedDirectory != null
            ? (!string.IsNullOrEmpty(SelectedDirectory.Name) ? SelectedDirectory.Name : "Library")
            : "Gameplay Library",
        _ => "Home"
    };

    public string CurrentDirectoryPath => CurrentSection switch
    {
        NavigationSection.Home => AllClips.Count > 0 ? "All watch folders" : string.Empty,
        NavigationSection.Favorites => FavoritesCount > 0 ? $"{FavoritesCount} favorite clips" : string.Empty,
        NavigationSection.SavedMoments => SavedMomentsCount > 0 ? $"{SavedMomentsCount} clips with moments" : string.Empty,
        NavigationSection.Folder => SelectedDirectory?.FullPath ?? string.Empty,
        _ => string.Empty
    };

    public string EmptyStateGlyph => CurrentSection switch
    {
        NavigationSection.Home => "\uE80F",
        NavigationSection.Favorites => "\uEB52",
        NavigationSection.SavedMoments => "\uE8A4",
        _ => "\uE8B7"
    };

    public string EmptyStateTitle => CurrentSection switch
    {
        NavigationSection.Home => "No clips in library",
        NavigationSection.Favorites => "No favorites yet",
        NavigationSection.SavedMoments => "No saved moments yet",
        _ => "No clips in this folder"
    };

    public string EmptyStateSubtitle => CurrentSection switch
    {
        NavigationSection.Home => "Add your captures folder or drop video files to start watching.",
        NavigationSection.Favorites => "Click the heart icon on any clip to pin it to your favorites.",
        NavigationSection.SavedMoments => "Add bookmarks and timestamps during video playback to revisit key highlights.",
        _ => "Choose another folder or add MP4/MKV video files to this directory."
    };

    partial void OnSelectedDirectoryChanged(DirectoryNode? value)
    {
        OnPropertyChanged(nameof(CurrentDirectoryTitle));
        OnPropertyChanged(nameof(CurrentDirectoryPath));
        OnPropertyChanged(nameof(CanOpenDirectoryInExplorer));
        OnPropertyChanged(nameof(ClipsCountSummary));
    }

    public int TotalClipsCount => Clips.Count;

    public string ClipsCountSummary =>
        $"{TotalClipsCount} {(TotalClipsCount == 1 ? "clip" : "clips")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarOpen))]
    private bool _isSidebarCollapsed;

    public bool IsSidebarOpen
    {
        get => !IsSidebarCollapsed;
        set
        {
            if (IsSidebarCollapsed == value)
            {
                IsSidebarCollapsed = !value;
            }
        }
    }

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    [RelayCommand]
    public void OpenCurrentDirectory()
    {
        string? path = CurrentDirectoryPath;
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    public string TotalStorageUsedFormatted
    {
        get
        {
            long total = Clips.Sum(c => c.FileSizeBytes);
            if (total < 1024) return $"{total} B";
            double mb = total / (1024.0 * 1024.0);
            if (mb < 1024) return $"{mb:F1} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }
    }

    public HomeViewModel(
        IClipIndexerService indexerService,
        ILocalStorageService storageService,
        INavigationService navigationService)
    {
        _indexerService = indexerService;
        _storageService = storageService;
        _navigationService = navigationService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _indexerService.ClipAdded += OnClipAdded;
        _indexerService.ClipDeleted += OnClipDeleted;
        _indexerService.ClipRenamed += OnClipRenamed;
    }

    public async Task InitializeAsync()
    {
        await SyncDirectoriesAsync();
    }

    [RelayCommand]
    public Task SelectHomeAsync()
    {
        CurrentSection = NavigationSection.Home;
        SelectedDirectory = null;
        RefreshCurrentViewClips();
        return Task.CompletedTask;
    }

    [RelayCommand]
    public Task SelectFavoritesAsync()
    {
        CurrentSection = NavigationSection.Favorites;
        SelectedDirectory = null;
        RefreshCurrentViewClips();
        return Task.CompletedTask;
    }

    [RelayCommand]
    public Task SelectSavedMomentsAsync()
    {
        CurrentSection = NavigationSection.SavedMoments;
        SelectedDirectory = null;
        RefreshCurrentViewClips();
        return Task.CompletedTask;
    }

    public Task SelectDirectoryAsync(DirectoryNode node)
    {
        CurrentSection = NavigationSection.Folder;
        SelectedDirectory = node;

        var settings = _storageService.CurrentSettings;
        settings.LastActiveDirectory = node.FullPath;
        _ = _storageService.SaveSettingsAsync(settings);

        RefreshCurrentViewClips();
        return Task.CompletedTask;
    }

    public async Task SyncDirectoriesAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading watch directories...";

        var settings = await _storageService.LoadSettingsAsync();

        var trees = await _indexerService.BuildDirectoryTreesAsync(settings.WatchDirectories);
        Directories.Clear();
        foreach (var tree in trees)
        {
            Directories.Add(tree);
        }

        _indexerService.UpdateWatchers(settings.WatchDirectories);
        OnPropertyChanged(nameof(HasDirectories));

        var allClipsList = new List<GameClip>();
        foreach (var dir in settings.WatchDirectories)
        {
            if (Directory.Exists(dir))
            {
                var dirClips = await _indexerService.ScanDirectoryClipsAsync(dir, recursive: true);
                allClipsList.AddRange(dirClips);
            }
        }

        AllClips.Clear();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in allClipsList)
        {
            if (!seenPaths.Add(clip.FilePath)) continue;

            if (settings.Favorites.Contains(clip.FilePath))
            {
                clip.IsFavorite = true;
            }

            if (settings.Bookmarks.TryGetValue(clip.FilePath, out var bms))
            {
                foreach (var bm in bms)
                {
                    clip.Bookmarks.Add(bm);
                }
            }

            AllClips.Add(clip);
        }

        IsLoading = false;

        // Restore last active selection or default to Home
        if (CurrentSection == NavigationSection.Folder && !string.IsNullOrEmpty(settings.LastActiveDirectory))
        {
            var target = FindNodeByPath(Directories, settings.LastActiveDirectory);
            if (target != null)
            {
                SelectedDirectory = target;
                RefreshCurrentViewClips();
            }
            else
            {
                await SelectHomeAsync();
            }
        }
        else if (CurrentSection == NavigationSection.Favorites)
        {
            await SelectFavoritesAsync();
        }
        else if (CurrentSection == NavigationSection.SavedMoments)
        {
            await SelectSavedMomentsAsync();
        }
        else
        {
            await SelectHomeAsync();
        }
    }

    private void RefreshCurrentViewClips()
    {
        Clips.Clear();
        switch (CurrentSection)
        {
            case NavigationSection.Home:
                foreach (var clip in AllClips)
                {
                    Clips.Add(clip);
                }
                StatusMessage = Clips.Count > 0 ? $"{Clips.Count} clips loaded" : "No clips in library";
                break;

            case NavigationSection.Favorites:
                foreach (var clip in AllClips.Where(c => c.IsFavorite))
                {
                    Clips.Add(clip);
                }
                StatusMessage = Clips.Count > 0 ? $"{Clips.Count} favorite clips" : "No favorites yet";
                break;

            case NavigationSection.SavedMoments:
                foreach (var clip in AllClips.Where(c => c.Bookmarks.Count > 0))
                {
                    Clips.Add(clip);
                }
                StatusMessage = Clips.Count > 0 ? $"{Clips.Count} clips with moments" : "No saved moments yet";
                break;

            case NavigationSection.Folder:
                if (SelectedDirectory != null)
                {
                    var folderClips = SelectedDirectory.IsWatchRoot
                        ? AllClips.Where(c => c.FilePath.StartsWith(SelectedDirectory.FullPath, StringComparison.OrdinalIgnoreCase))
                        : AllClips.Where(c => string.Equals(c.DirectoryPath, SelectedDirectory.FullPath, StringComparison.OrdinalIgnoreCase));

                    foreach (var clip in folderClips)
                    {
                        Clips.Add(clip);
                    }
                    SelectedDirectory.ClipCount = Clips.Count;
                    StatusMessage = Clips.Count > 0 ? $"{Clips.Count} clips loaded" : "No clips in this folder";
                }
                break;
        }

        ApplyFilterAndSort();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(AllClipsCount));
        OnPropertyChanged(nameof(FavoritesCount));
        OnPropertyChanged(nameof(SavedMomentsCount));
        OnPropertyChanged(nameof(TotalClipsCount));
        OnPropertyChanged(nameof(ClipsCountSummary));
        OnPropertyChanged(nameof(TotalStorageUsedFormatted));
        OnPropertyChanged(nameof(CurrentDirectoryPath));
        OnPropertyChanged(nameof(HasClips));
    }

    [RelayCommand]
    public async Task AddDirectoryAsync(IntPtr hwnd)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                string path = folder.Path;
                var settings = _storageService.CurrentSettings;
                if (!settings.WatchDirectories.Contains(path))
                {
                    settings.WatchDirectories.Add(path);
                    await _storageService.SaveSettingsAsync(settings);
                    await SyncDirectoriesAsync();

                    var newNode = FindNodeByPath(Directories, path);
                    if (newNode != null)
                    {
                        await SelectDirectoryAsync(newNode);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error adding folder: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RemoveDirectoryAsync(DirectoryNode node)
    {
        if (node == null) return;

        var settings = _storageService.CurrentSettings;
        settings.WatchDirectories.Remove(node.FullPath);
        await _storageService.SaveSettingsAsync(settings);
        await SyncDirectoriesAsync();
    }

    [RelayCommand]
    public async Task RefreshClipsAsync()
    {
        await SyncDirectoriesAsync();
    }

    [RelayCommand]
    public void PlayClip(GameClip? clip)
    {
        clip ??= SelectedClip;
        if (clip == null) return;

        _navigationService.NavigateToPlayer(clip, FilteredClips);
    }

    [RelayCommand]
    public void DeleteClip(GameClip clip)
    {
        if (clip == null) return;

        bool success = _indexerService.DeleteClipToRecycleBin(clip.FilePath);
        if (success)
        {
            AllClips.Remove(clip);
            Clips.Remove(clip);
            FilteredClips.Remove(clip);
            if (SelectedDirectory != null)
            {
                SelectedDirectory.ClipCount = Clips.Count;
            }
            UpdateCounts();
            StatusMessage = $"Deleted {clip.FileName} to Recycle Bin";
        }
        else
        {
            StatusMessage = $"Failed to delete {clip.FileName}";
        }
    }

    [RelayCommand]
    public void RenameClip(GameClip clip)
    {
        // View code-behind or dialog will prompt for new name then call RenameClipInternal
    }

    public bool RenameClipInternal(GameClip clip, string newName)
    {
        if (clip == null || string.IsNullOrWhiteSpace(newName)) return false;

        if (_indexerService.RenameClip(clip.FilePath, newName, out string newPath))
        {
            clip.FilePath = newPath;
            clip.FileName = Path.GetFileName(newPath);
            StatusMessage = $"Renamed to {clip.FileName}";
            return true;
        }
        StatusMessage = "Failed to rename clip";
        return false;
    }

    [RelayCommand]
    public void OpenInExplorer(GameClip clip)
    {
        if (clip == null || !File.Exists(clip.FilePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{clip.FilePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error opening Explorer: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CopyPath(GameClip clip)
    {
        if (clip == null) return;

        var package = new DataPackage();
        package.SetText(clip.FilePath);
        Clipboard.SetContent(package);
        StatusMessage = "Copied file path to clipboard";
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(GameClip clip)
    {
        if (clip == null) return;

        clip.IsFavorite = !clip.IsFavorite;
        var settings = _storageService.CurrentSettings;
        if (clip.IsFavorite)
        {
            settings.Favorites.Add(clip.FilePath);
        }
        else
        {
            settings.Favorites.Remove(clip.FilePath);
        }
        await _storageService.SaveSettingsAsync(settings);

        if (CurrentSection == NavigationSection.Favorites && !clip.IsFavorite)
        {
            Clips.Remove(clip);
            ApplyFilterAndSort();
        }

        UpdateCounts();
    }

    [RelayCommand]
    public void OpenSettings()
    {
        _navigationService.NavigateToSettings();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilterAndSort();
    }

    partial void OnSortIndexChanged(int value)
    {
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<GameClip> query = Clips;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            query = query.Where(c => c.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        query = SortIndex switch
        {
            0 => query.OrderByDescending(c => c.CreatedDate != DateTime.MinValue ? c.CreatedDate : c.ModifiedDate),
            1 => query.OrderBy(c => c.CreatedDate != DateTime.MinValue ? c.CreatedDate : c.ModifiedDate),
            2 => query.OrderBy(c => c.FileName, StringComparer.OrdinalIgnoreCase),
            3 => query.OrderByDescending(c => c.Duration),
            4 => query.OrderByDescending(c => c.FileSizeBytes),
            _ => query
        };

        FilteredClips.Clear();
        foreach (var c in query)
        {
            FilteredClips.Add(c);
        }
        OnPropertyChanged(nameof(HasClips));
    }

    private void OnClipAdded(string filePath)
    {
        _dispatcherQueue.TryEnqueue(async () =>
        {
            await SyncDirectoriesAsync();
        });
    }

    private void OnClipDeleted(string filePath)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var existingAll = AllClips.FirstOrDefault(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (existingAll != null)
            {
                AllClips.Remove(existingAll);
            }

            var existing = Clips.FirstOrDefault(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                Clips.Remove(existing);
                FilteredClips.Remove(existing);
                if (SelectedDirectory != null)
                {
                    SelectedDirectory.ClipCount = Clips.Count;
                }
            }
            UpdateCounts();
        });
    }

    private void OnClipRenamed(string oldPath, string newPath)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var existingAll = AllClips.FirstOrDefault(c => string.Equals(c.FilePath, oldPath, StringComparison.OrdinalIgnoreCase));
            if (existingAll != null)
            {
                existingAll.FilePath = newPath;
                existingAll.FileName = Path.GetFileName(newPath);
            }

            var existing = Clips.FirstOrDefault(c => string.Equals(c.FilePath, oldPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.FilePath = newPath;
                existing.FileName = Path.GetFileName(newPath);
            }
        });
    }

    public static DirectoryNode? FindNodeByPath(IEnumerable<DirectoryNode> nodes, string path)
    {
        foreach (var n in nodes)
        {
            if (string.Equals(n.FullPath, path, StringComparison.OrdinalIgnoreCase))
                return n;

            var sub = FindNodeByPath(n.SubDirectories, path);
            if (sub != null)
                return sub;
        }
        return null;
    }
}
