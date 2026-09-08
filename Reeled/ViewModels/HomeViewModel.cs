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

public partial class HomeViewModel : ObservableObject
{
    private readonly IClipIndexerService _indexerService;
    private readonly ILocalStorageService _storageService;
    private readonly INavigationService _navigationService;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<DirectoryNode> Directories { get; } = new();
    public ObservableCollection<GameClip> Clips { get; } = new();
    public ObservableCollection<GameClip> FilteredClips { get; } = new();

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

    public int TotalClipsCount => Clips.Count;

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

        // Restore last active directory or first available
        DirectoryNode? target = null;
        if (!string.IsNullOrEmpty(settings.LastActiveDirectory))
        {
            target = FindNodeByPath(Directories, settings.LastActiveDirectory);
        }
        target ??= Directories.FirstOrDefault();

        if (target != null)
        {
            await SelectDirectoryAsync(target);
        }
        else
        {
            IsLoading = false;
            StatusMessage = "No folders added. Click '+' to add a folder with clips.";
        }
    }

    public async Task SelectDirectoryAsync(DirectoryNode node)
    {
        SelectedDirectory = node;
        IsLoading = true;
        StatusMessage = $"Scanning {node.Name}...";

        var settings = _storageService.CurrentSettings;
        settings.LastActiveDirectory = node.FullPath;
        _ = _storageService.SaveSettingsAsync(settings);

        var clips = await _indexerService.ScanDirectoryClipsAsync(node.FullPath);

        Clips.Clear();
        foreach (var clip in clips)
        {
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

            Clips.Add(clip);
        }

        node.ClipCount = clips.Count;
        ApplyFilterAndSort();

        IsLoading = false;
        StatusMessage = Clips.Count > 0 ? $"{Clips.Count} clips loaded" : "No clips in this folder";
        OnPropertyChanged(nameof(TotalClipsCount));
        OnPropertyChanged(nameof(TotalStorageUsedFormatted));
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

                    var trees = await _indexerService.BuildDirectoryTreesAsync(settings.WatchDirectories);
                    Directories.Clear();
                    foreach (var tree in trees)
                    {
                        Directories.Add(tree);
                    }

                    _indexerService.UpdateWatchers(settings.WatchDirectories);

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

        Directories.Remove(node);
        _indexerService.UpdateWatchers(settings.WatchDirectories);

        if (SelectedDirectory == node)
        {
            var next = Directories.FirstOrDefault();
            if (next != null)
            {
                await SelectDirectoryAsync(next);
            }
            else
            {
                Clips.Clear();
                FilteredClips.Clear();
                SelectedDirectory = null;
                StatusMessage = "Add a folder to get started";
            }
        }
    }

    [RelayCommand]
    public async Task RefreshClipsAsync()
    {
        if (SelectedDirectory != null)
        {
            await SelectDirectoryAsync(SelectedDirectory);
        }
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
            Clips.Remove(clip);
            FilteredClips.Remove(clip);
            if (SelectedDirectory != null)
            {
                SelectedDirectory.ClipCount = Clips.Count;
            }
            OnPropertyChanged(nameof(TotalClipsCount));
            OnPropertyChanged(nameof(TotalStorageUsedFormatted));
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
    }

    private void OnClipAdded(string filePath)
    {
        _dispatcherQueue.TryEnqueue(async () =>
        {
            if (SelectedDirectory != null &&
                string.Equals(Path.GetDirectoryName(filePath), SelectedDirectory.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                await RefreshClipsAsync();
            }
        });
    }

    private void OnClipDeleted(string filePath)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var existing = Clips.FirstOrDefault(c => string.Equals(c.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                Clips.Remove(existing);
                FilteredClips.Remove(existing);
                if (SelectedDirectory != null)
                {
                    SelectedDirectory.ClipCount = Clips.Count;
                }
                OnPropertyChanged(nameof(TotalClipsCount));
                OnPropertyChanged(nameof(TotalStorageUsedFormatted));
            }
        });
    }

    private void OnClipRenamed(string oldPath, string newPath)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var existing = Clips.FirstOrDefault(c => string.Equals(c.FilePath, oldPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.FilePath = newPath;
                existing.FileName = Path.GetFileName(newPath);
            }
        });
    }

    private static DirectoryNode? FindNodeByPath(IEnumerable<DirectoryNode> nodes, string path)
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
