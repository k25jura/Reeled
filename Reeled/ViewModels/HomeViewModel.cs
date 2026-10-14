using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Microsoft.UI.Dispatching;
using Reeled.Helpers;
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
    private readonly ILocalizationService _localizationService;
    private readonly IClipMetadataService _metadataService;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableRangeCollection<DirectoryNode> Directories { get; } = new();
    public ObservableRangeCollection<GameClip> AllClips { get; } = new();
    public ObservableRangeCollection<GameClip> Clips { get; } = new();
    public ObservableRangeCollection<GameClip> FilteredClips { get; } = new();
    public ObservableRangeCollection<ClipGroup> GroupedClips { get; } = new();

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

    public event Action? RequestScrollToTop;

    /// <summary>Raised after the visible clip set was rebuilt for a different section or folder.</summary>
    public event Action? ViewContentReplaced;

    public bool CanOpenDirectoryInExplorer =>
        CurrentSection == NavigationSection.Folder && !string.IsNullOrEmpty(SelectedDirectory?.FullPath);

    [ObservableProperty]
    private DirectoryNode? _selectedDirectory;

    [ObservableProperty]
    private GameClip? _selectedClip;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _sortIndex = 0; // 0: Newest, 1: Oldest, 2: Name A-Z, 3: Name Z-A, 4: Duration, 5: Size

    [ObservableProperty]
    private DateGroupingMode _dateGrouping = DateGroupingMode.None;

    partial void OnDateGroupingChanged(DateGroupingMode value)
    {
        var settings = _storageService.CurrentSettings;
        settings.DateGrouping = value;
        _ = _storageService.SaveSettingsAsync(settings);
        RegroupClips();
    }

    [ObservableProperty]
    private ViewDensityMode _viewDensity = ViewDensityMode.Comfortable;

    partial void OnViewDensityChanged(ViewDensityMode value)
    {
        var settings = _storageService.CurrentSettings;
        settings.ViewDensity = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [ObservableProperty]
    private bool _metadataHoverOnly = false;

    partial void OnMetadataHoverOnlyChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        settings.MetadataHoverOnly = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowSkeletonLoading))]
    [NotifyPropertyChangedFor(nameof(ShowProgressRingLoading))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSkeletonLoading))]
    [NotifyPropertyChangedFor(nameof(ShowProgressRingLoading))]
    private bool _enableSkeletonLoading = true;

    [ObservableProperty]
    private bool _showMomentsBadges = true;

    partial void OnShowMomentsBadgesChanged(bool value)
    {
        _dispatcherQueue.TryEnqueue(RefreshMomentsBadgeVisibility);
    }

    public void RefreshMomentsBadgeVisibility()
    {
        foreach (var clip in AllClips)
        {
            clip.NotifyMomentsBadgeChanged();
        }
    }

    public void UpdateBookmarkCounts()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            UpdateCounts();
            RefreshMomentsBadgeVisibility();
            if (CurrentSection == NavigationSection.SavedMoments)
            {
                // Clips is the section's base set: a clip whose last moment was removed must leave it,
                // and one that gained its first moment must join it
                var withMoments = AllClips.Where(c => c.Bookmarks.Count > 0).ToList();
                if (withMoments.Count != Clips.Count || withMoments.Any(c => !Clips.Contains(c)))
                {
                    Clips.ReplaceRange(withMoments);
                }
                ApplyFilterAndSort();
                UpdateCounts();
            }
        });
    }

    public bool ShowSkeletonLoading => IsLoading && EnableSkeletonLoading;
    public bool ShowProgressRingLoading => IsLoading && !EnableSkeletonLoading;

    public int[] SkeletonPlaceholders { get; } = new int[36];
    private readonly HashSet<string> _loadedWatchDirectories = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasDirectories => Directories.Count > 0;
    public bool HasClips => FilteredClips.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasClips;

    public string CurrentDirectoryTitle => CurrentSection switch
    {
        NavigationSection.Home => _localizationService["Nav_Home"],
        NavigationSection.Favorites => _localizationService["Nav_Favorites"],
        NavigationSection.SavedMoments => _localizationService["Nav_SavedMoments"],
        NavigationSection.Folder => SelectedDirectory != null
            ? (!string.IsNullOrEmpty(SelectedDirectory.Name) ? SelectedDirectory.Name : _localizationService["Breadcrumb_Library"])
            : _localizationService["Breadcrumb_GameplayLibrary"],
        _ => _localizationService["Nav_Home"]
    };

    public string CurrentDirectoryPath => CurrentSection switch
    {
        NavigationSection.Home => AllClips.Count > 0 ? _localizationService["Breadcrumb_AllWatchFolders"] : string.Empty,
        NavigationSection.Favorites => FavoritesCount > 0 ? string.Format(_localizationService["Breadcrumb_FavoriteClips"], FavoritesCount) : string.Empty,
        NavigationSection.SavedMoments => SavedMomentsCount > 0 ? string.Format(_localizationService["Breadcrumb_ClipsWithMoments"], SavedMomentsCount) : string.Empty,
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
        NavigationSection.Home => _localizationService["Empty_NoClipsTitle"],
        NavigationSection.Favorites => _localizationService["Empty_NoFavoritesTitle"],
        NavigationSection.SavedMoments => _localizationService["Empty_NoMomentsTitle"],
        _ => _localizationService["Empty_FolderTitle"]
    };

    public string EmptyStateSubtitle => CurrentSection switch
    {
        NavigationSection.Home => _localizationService["Empty_NoClipsSubtitle"],
        NavigationSection.Favorites => _localizationService["Empty_NoFavoritesSubtitle"],
        NavigationSection.SavedMoments => _localizationService["Empty_NoMomentsSubtitle"],
        _ => _localizationService["Empty_FolderSubtitle"]
    };

    partial void OnSelectedDirectoryChanged(DirectoryNode? value)
    {
        OnPropertyChanged(nameof(CurrentDirectoryTitle));
        OnPropertyChanged(nameof(CurrentDirectoryPath));
        OnPropertyChanged(nameof(CanOpenDirectoryInExplorer));
        OnPropertyChanged(nameof(ClipsCountSummary));
        OnPropertyChanged(nameof(ClipsUnitLabel));
    }

    public int TotalClipsCount => Clips.Count;

    public string ClipsUnitLabel =>
        _localizationService.FormatPlural("Plural_Clip", TotalClipsCount);

    public string ClipsCountSummary =>
        $"{TotalClipsCount} {ClipsUnitLabel}";

    public void RefreshSavedMoments()
    {
        OnPropertyChanged(nameof(SavedMomentsCount));
        if (CurrentSection == NavigationSection.SavedMoments)
        {
            RefreshCurrentViewClips();
        }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(CurrentDirectoryTitle));
        OnPropertyChanged(nameof(CurrentDirectoryPath));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateSubtitle));
        OnPropertyChanged(nameof(ClipsCountSummary));
        OnPropertyChanged(nameof(ClipsUnitLabel));
        OnPropertyChanged(nameof(SavedMomentsCount));
        OnPropertyChanged(nameof(TotalStorageUsedFormatted));
        foreach (var clip in AllClips)
        {
            clip.RefreshFormattedStrings();
        }
        RegroupClips();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarOpen))]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private double _sidebarWidth = 316;

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

    public void SaveSidebarWidth(double width)
    {
        SidebarWidth = width;
        var settings = _storageService.CurrentSettings;
        settings.SidebarWidth = width;
        _ = _storageService.SaveSettingsAsync(settings);
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
            var loc = _localizationService ?? App.GetService<ILocalizationService>();
            string bUnit = loc?["Unit_Byte"] ?? "B";
            string kbUnit = loc?["Unit_KB"] ?? "KB";
            string mbUnit = loc?["Unit_MB"] ?? "MB";
            string gbUnit = loc?["Unit_GB"] ?? "GB";
            var culture = loc?.CurrentCulture ?? System.Globalization.CultureInfo.InvariantCulture;

            long total = Clips.Sum(c => c.FileSizeBytes);
            if (total < 1024) return string.Format(culture, "{0} {1}", total, bUnit);
            double mb = total / (1024.0 * 1024.0);
            if (mb < 1024) return string.Format(culture, "{0:F1} {1}", mb, mbUnit);
            double gb = mb / 1024.0;
            return string.Format(culture, "{0:F2} {1}", gb, gbUnit);
        }
    }

    public HomeViewModel(
        IClipIndexerService indexerService,
        ILocalStorageService storageService,
        INavigationService navigationService,
        ILocalizationService localizationService,
        IClipMetadataService metadataService)
    {
        _indexerService = indexerService;
        _storageService = storageService;
        _navigationService = navigationService;
        _localizationService = localizationService;
        _metadataService = metadataService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var settings = _storageService.CurrentSettings;
        _enableSkeletonLoading = settings.EnableSkeletonLoading;
        _showMomentsBadges = settings.ShowMomentsBadges;
        _dateGrouping = settings.DateGrouping;
        _viewDensity = settings.ViewDensity;
        _metadataHoverOnly = settings.MetadataHoverOnly;
        _sortIndex = settings.SortIndex;
        if (settings.SidebarWidth > 0)
        {
            _sidebarWidth = settings.SidebarWidth;
        }

        _indexerService.ClipAdded += OnClipAdded;
        _indexerService.ClipDeleted += OnClipDeleted;
        _indexerService.ClipRenamed += OnClipRenamed;
        _indexerService.LibraryChanged += () => _dispatcherQueue.TryEnqueue(async () => await SyncDirectoriesAsync(forceReload: true));
        _indexerService.MetadataBatchCompleted += () => _dispatcherQueue.TryEnqueue(() =>
        {
            // Durations arrive in the background; re-sort once they're known so "Duration" ordering is correct
            if (SortIndex == 4)
            {
                ApplyFilterAndSort();
            }
        });

        _localizationService.LanguageChanged += (s, e) =>
        {
            _dispatcherQueue.TryEnqueue(RefreshLocalization);
        };
    }

    public async Task InitializeAsync()
    {
        EnableSkeletonLoading = _storageService.CurrentSettings.EnableSkeletonLoading;
        ShowMomentsBadges = _storageService.CurrentSettings.ShowMomentsBadges;
        DateGrouping = _storageService.CurrentSettings.DateGrouping;
        ViewDensity = _storageService.CurrentSettings.ViewDensity;
        MetadataHoverOnly = _storageService.CurrentSettings.MetadataHoverOnly;
        SortIndex = _storageService.CurrentSettings.SortIndex;
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

    public async Task SelectDirectoryAsync(DirectoryNode node)
    {
        if (CurrentSection == NavigationSection.Folder && SelectedDirectory == node)
        {
            return;
        }

        CurrentSection = NavigationSection.Folder;
        SelectedDirectory = node;

        var settings = _storageService.CurrentSettings;
        settings.LastActiveDirectory = node.FullPath;
        _ = _storageService.SaveSettingsAsync(settings);

        // Yield to allow click pulse and sidebar animations to start immediately without UI thread contention
        await Task.Yield();

        RefreshCurrentViewClips();
    }

    public async Task SelectDirectoryByPathAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var node = FindNodeByPath(Directories, path);
        if (node != null)
        {
            await SelectDirectoryAsync(node);
        }
    }

    public void ClearThumbnailsInMemory()
    {
        foreach (var clip in AllClips)
        {
            clip.Thumbnail = null;
        }
        foreach (var clip in FilteredClips)
        {
            clip.Thumbnail = null;
        }
    }

    private Task? _syncTask;
    private bool _resyncRequested;

    public async Task SyncDirectoriesAsync(bool forceReload = false)
    {
        // Coalesce overlapping requests (watcher events, folder adds) into one follow-up rescan.
        // Callers still await completion so they can rely on the refreshed tree.
        if (_syncTask != null)
        {
            _resyncRequested = true;
            await _syncTask;
            return;
        }

        _syncTask = RunSyncLoopAsync(forceReload);
        try
        {
            await _syncTask;
        }
        finally
        {
            _syncTask = null;
        }
    }

    private async Task RunSyncLoopAsync(bool forceReload)
    {
        await SyncDirectoriesCoreAsync(forceReload);
        while (_resyncRequested)
        {
            _resyncRequested = false;
            await SyncDirectoriesCoreAsync(forceReload: true);
        }
    }

    private async Task SyncDirectoriesCoreAsync(bool forceReload)
    {
        var settings = await _storageService.LoadSettingsAsync();
        EnableSkeletonLoading = settings.EnableSkeletonLoading;
        ShowMomentsBadges = settings.ShowMomentsBadges;
        DateGrouping = settings.DateGrouping;
        ViewDensity = settings.ViewDensity;
        MetadataHoverOnly = settings.MetadataHoverOnly;
        SortIndex = settings.SortIndex;
        if (settings.SidebarWidth >= 200 && settings.SidebarWidth <= 600)
        {
            SidebarWidth = settings.SidebarWidth;
        }

        bool dirsChanged = !_loadedWatchDirectories.SetEquals(settings.WatchDirectories);
        if (!forceReload && !dirsChanged && AllClips.Count > 0 && Directories.Count > 0)
        {
            // Watch folders haven't changed and clips are already in memory - instant view restore
            RefreshCurrentViewClips();
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading watch directories...";

        var expandedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void CollectExpanded(IEnumerable<DirectoryNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsExpanded) expandedPaths.Add(node.FullPath);
                CollectExpanded(node.SubDirectories);
            }
        }
        CollectExpanded(Directories);

        var (processedClips, processedTrees) = await Task.Run(async () =>
        {
            var trees = await _indexerService.BuildDirectoryTreesAsync(settings.WatchDirectories);
            void RestoreExpanded(IEnumerable<DirectoryNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (expandedPaths.Contains(node.FullPath))
                    {
                        node.IsExpanded = true;
                    }
                    RestoreExpanded(node.SubDirectories);
                }
            }
            RestoreExpanded(trees);

            var allClipsList = new List<GameClip>();
            foreach (var dir in settings.WatchDirectories)
            {
                if (Directory.Exists(dir))
                {
                    var dirClips = await _indexerService.ScanDirectoryClipsAsync(dir, recursive: true);
                    allClipsList.AddRange(dirClips);
                }
            }

            var list = new List<GameClip>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var clip in allClipsList)
            {
                if (!seenPaths.Add(clip.FilePath)) continue;
                ApplyPersistedState(clip, settings);
                list.Add(clip);
            }

            void UpdateNodeClipCounts(DirectoryNode node)
            {
                foreach (var sub in node.SubDirectories)
                {
                    UpdateNodeClipCounts(sub);
                }

                node.ClipCount = list.Count(c => PathUtil.IsUnder(c.FilePath, node.FullPath));
            }

            foreach (var tree in trees)
            {
                UpdateNodeClipCounts(tree);
            }

            return (list, trees);
        });

        _indexerService.UpdateWatchers(settings.WatchDirectories);

        AllClips.ReplaceRange(processedClips);
        // Merge instead of replacing: a full Reset made the TreeView tear down and rebuild every row,
        // so folders blinked out and back in on each refresh and lost their realized containers
        MergeDirectoryNodes(Directories, processedTrees);
        OnPropertyChanged(nameof(HasDirectories));

        _loadedWatchDirectories.Clear();
        foreach (var d in settings.WatchDirectories)
        {
            _loadedWatchDirectories.Add(d);
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
        List<GameClip> targetClips;
        switch (CurrentSection)
        {
            case NavigationSection.Home:
                targetClips = AllClips.ToList();
                StatusMessage = targetClips.Count > 0 ? $"{targetClips.Count} clips loaded" : "No clips in library";
                break;

            case NavigationSection.Favorites:
                targetClips = AllClips.Where(c => c.IsFavorite).ToList();
                StatusMessage = targetClips.Count > 0 ? $"{targetClips.Count} favorite clips" : "No favorites yet";
                break;

            case NavigationSection.SavedMoments:
                targetClips = AllClips.Where(c => c.Bookmarks.Count > 0).ToList();
                StatusMessage = targetClips.Count > 0 ? $"{targetClips.Count} clips with moments" : "No saved moments yet";
                break;

            case NavigationSection.Folder:
                if (SelectedDirectory != null)
                {
                    // Every folder shows its whole subtree, matching the count shown next to it in the sidebar
                    var folderPath = SelectedDirectory.FullPath;
                    targetClips = AllClips.Where(c => PathUtil.IsUnder(c.FilePath, folderPath)).ToList();

                    SelectedDirectory.ClipCount = targetClips.Count;
                    StatusMessage = targetClips.Count > 0 ? $"{targetClips.Count} clips loaded" : "No clips in this folder";
                }
                else
                {
                    targetClips = new List<GameClip>();
                }
                break;

            default:
                targetClips = new List<GameClip>();
                break;
        }

        Clips.ReplaceRange(targetClips);
        ApplyFilterAndSort();
        UpdateCounts();
        ViewContentReplaced?.Invoke();
        RequestScrollToTop?.Invoke();
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(AllClipsCount));
        OnPropertyChanged(nameof(FavoritesCount));
        OnPropertyChanged(nameof(SavedMomentsCount));
        OnPropertyChanged(nameof(TotalClipsCount));
        OnPropertyChanged(nameof(ClipsCountSummary));
        OnPropertyChanged(nameof(ClipsUnitLabel));
        OnPropertyChanged(nameof(TotalStorageUsedFormatted));
        OnPropertyChanged(nameof(CurrentDirectoryPath));
        OnPropertyChanged(nameof(HasClips));
        OnPropertyChanged(nameof(ShowEmptyState));
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
                await AddDirectoryPathAsync(folder.Path);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error adding folder: {ex.Message}";
        }
    }

    public async Task AddDirectoryPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            var settings = _storageService.CurrentSettings;

            // A folder inside an existing watch folder is already indexed: just open it
            if (!settings.WatchDirectories.Any(root => PathUtil.IsUnder(path, root)))
            {
                settings.WatchDirectories.Add(path);
                await _storageService.SaveSettingsAsync(settings);
                await SyncDirectoriesAsync();
            }

            var node = FindNodeByPath(Directories, path);
            if (node != null)
            {
                await SelectDirectoryAsync(node);
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
        await SyncDirectoriesAsync(forceReload: true);
    }

    private long _lastPlayClipTimestamp;

    [RelayCommand]
    public void PlayClip(GameClip? clip)
    {
        clip ??= SelectedClip;
        if (clip == null) return;

        long now = Environment.TickCount64;
        if (now - _lastPlayClipTimestamp < 400)
        {
            return;
        }
        _lastPlayClipTimestamp = now;

        _navigationService.NavigateToPlayer(clip, FilteredClips);
    }

    public bool DeleteClip(GameClip clip)
    {
        if (clip == null) return false;

        string path = clip.FilePath;
        bool success = _indexerService.DeleteClipToRecycleBin(path);
        if (success)
        {
            RemoveClipFromLibrary(path);
            ForgetClipState(path);
            StatusMessage = $"Deleted {clip.FileName} to Recycle Bin";
        }
        else
        {
            StatusMessage = $"Failed to delete {clip.FileName}";
        }
        return success;
    }

    public bool RenameClipInternal(GameClip clip, string newName)
    {
        if (clip == null || string.IsNullOrWhiteSpace(newName)) return false;

        string oldPath = clip.FilePath;
        if (_indexerService.RenameClip(oldPath, newName, out string newPath))
        {
            ApplyRename(oldPath, newPath);
            StatusMessage = $"Renamed to {clip.FileName}";
            return true;
        }
        StatusMessage = "Failed to rename clip";
        return false;
    }

    /// <summary>
    /// Returns the library's instance for a file (so favorites and moments stay shared with the player),
    /// or a new clip carrying the persisted favorite/moments/title state.
    /// </summary>
    public GameClip ResolveOrCreateClip(FileInfo fileInfo)
    {
        var existing = FindClip(fileInfo.FullName);
        if (existing != null) return existing;

        var clip = GameClip.FromFileInfo(fileInfo);
        ApplyPersistedState(clip, _storageService.CurrentSettings);
        return clip;
    }

    private GameClip? FindClip(string path) =>
        AllClips.FirstOrDefault(c => string.Equals(c.FilePath, path, StringComparison.OrdinalIgnoreCase));

    private static void ApplyPersistedState(GameClip clip, AppSettings settings)
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

        if (settings.CustomClipTitles.TryGetValue(clip.FilePath, out var customTitle) && !string.IsNullOrWhiteSpace(customTitle))
        {
            clip.CustomTitle = customTitle;
        }
    }

    /// <summary>Moves per-clip settings (favorite, moments, custom title, resume position) to a new path.</summary>
    private void MigrateClipState(string oldPath, string newPath)
    {
        var settings = _storageService.CurrentSettings;
        bool changed = false;

        if (settings.Favorites.Remove(oldPath))
        {
            settings.Favorites.Add(newPath);
            changed = true;
        }
        if (settings.Bookmarks.Remove(oldPath, out var bookmarks))
        {
            settings.Bookmarks[newPath] = bookmarks;
            changed = true;
        }
        if (settings.CustomClipTitles.Remove(oldPath, out var title))
        {
            settings.CustomClipTitles[newPath] = title;
            changed = true;
        }
        if (settings.ResumePositions.Remove(oldPath, out var position))
        {
            settings.ResumePositions[newPath] = position;
            changed = true;
        }

        if (changed)
        {
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    private void ForgetClipState(string path)
    {
        var settings = _storageService.CurrentSettings;
        bool changed = settings.Favorites.Remove(path);
        changed |= settings.Bookmarks.Remove(path);
        changed |= settings.CustomClipTitles.Remove(path);
        changed |= settings.ResumePositions.Remove(path);
        if (changed)
        {
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    private void ApplyRename(string oldPath, string newPath)
    {
        MigrateClipState(oldPath, newPath);

        var clip = FindClip(oldPath);
        if (clip == null) return;

        clip.FilePath = newPath;
        clip.FileName = Path.GetFileName(newPath);
        clip.DirectoryPath = Path.GetDirectoryName(newPath) ?? clip.DirectoryPath;
        ApplyFilterAndSort();
    }

    private void RemoveClipFromLibrary(string path)
    {
        var clip = FindClip(path);
        if (clip == null) return;

        AllClips.Remove(clip);
        Clips.Remove(clip);
        AdjustFolderCounts(path, -1);
        ApplyFilterAndSort();
        UpdateCounts();
    }

    private void AdjustFolderCounts(string filePath, int delta)
    {
        void Visit(IEnumerable<DirectoryNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (PathUtil.IsUnder(filePath, node.FullPath))
                {
                    node.ClipCount = Math.Max(0, node.ClipCount + delta);
                    Visit(node.SubDirectories);
                }
            }
        }
        Visit(Directories);
    }

    private bool ClipBelongsToCurrentView(GameClip clip) => CurrentSection switch
    {
        NavigationSection.Home => true,
        NavigationSection.Favorites => clip.IsFavorite,
        NavigationSection.SavedMoments => clip.Bookmarks.Count > 0,
        NavigationSection.Folder => SelectedDirectory != null && PathUtil.IsUnder(clip.FilePath, SelectedDirectory.FullPath),
        _ => false
    };

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

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpVerb;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpFile;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? lpParameters;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOW = 5;

    [RelayCommand]
    public void OpenFileProperties(GameClip? clip)
    {
        if (clip == null || !File.Exists(clip.FilePath)) return;

        try
        {
            var sei = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                lpVerb = "properties",
                lpFile = clip.FilePath,
                nShow = SW_SHOW,
                fMask = SEE_MASK_INVOKEIDLIST,
                hwnd = App.WindowHandle
            };
            ShellExecuteEx(ref sei);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error opening file properties: {ex.Message}";
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

    public async Task SetClipCustomTitleAsync(GameClip? clip, string? newTitle)
    {
        if (clip == null) return;
        newTitle = string.IsNullOrWhiteSpace(newTitle) ? null : newTitle.Trim();

        clip.CustomTitle = newTitle;
        var settings = _storageService.CurrentSettings;
        if (newTitle == null)
        {
            settings.CustomClipTitles.Remove(clip.FilePath);
        }
        else
        {
            settings.CustomClipTitles[clip.FilePath] = newTitle;
        }
        await _storageService.SaveSettingsAsync(settings);
        StatusMessage = newTitle != null ? $"Title updated: {newTitle}" : "Reset to original file name";
        ApplyFilterAndSort();
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
        var settings = _storageService.CurrentSettings;
        settings.SortIndex = value;
        _ = _storageService.SaveSettingsAsync(settings);
        ApplyFilterAndSort();
        RequestScrollToTop?.Invoke();
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<GameClip> query = Clips;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string term = SearchQuery.Trim();
            query = query.Where(c => c.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                     c.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                     Path.GetFileName(c.DirectoryPath).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                     c.Bookmarks.Any(b => b.Label.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        query = SortIndex switch
        {
            0 => query.OrderByDescending(c => c.EffectiveDate),
            1 => query.OrderBy(c => c.EffectiveDate),
            2 => query.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase),
            3 => query.OrderByDescending(c => c.DisplayName, StringComparer.OrdinalIgnoreCase),
            4 => query.OrderByDescending(c => c.Duration),
            5 => query.OrderByDescending(c => c.FileSizeBytes),
            _ => query
        };

        var filteredList = query.ToList();
        FilteredClips.ReplaceRange(filteredList);

        RegroupClips();
        OnPropertyChanged(nameof(HasClips));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    public void RegroupClips()
    {
        if (FilteredClips.Count == 0)
        {
            GroupedClips.Clear();
            return;
        }

        if (DateGrouping == DateGroupingMode.None)
        {
            var flatGroup = new ClipGroup("all", string.Empty, string.Empty, FilteredClips, isHeaderVisible: false);
            GroupedClips.ReplaceRange(new[] { flatGroup });
            return;
        }

        var loc = _localizationService ?? App.GetService<ILocalizationService>();
        var culture = loc?.CurrentCulture ?? System.Globalization.CultureInfo.CurrentCulture;
        string todayStr = loc?["Date_Today"] ?? "Today";
        string yesterdayStr = loc?["Date_Yesterday"] ?? "Yesterday";
        var now = DateTime.Now.Date;

        IEnumerable<IGrouping<string, GameClip>> groups = DateGrouping switch
        {
            // Dates are stored in UTC; bucket by local date so clips near midnight land under the right heading
            DateGroupingMode.Day => FilteredClips.GroupBy(c => c.EffectiveDate.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            DateGroupingMode.Month => FilteredClips.GroupBy(c => c.EffectiveDate.ToLocalTime().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)),
            DateGroupingMode.Year => FilteredClips.GroupBy(c => c.EffectiveDate.ToLocalTime().ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture)),
            _ => FilteredClips.GroupBy(c => "all")
        };

        groups = SortIndex switch
        {
            1 => groups.OrderBy(g => g.Min(c => c.EffectiveDate)),
            2 => groups.OrderBy(g => g.Min(c => c.DisplayName), StringComparer.OrdinalIgnoreCase),
            3 => groups.OrderByDescending(g => g.Max(c => c.DisplayName), StringComparer.OrdinalIgnoreCase),
            4 => groups.OrderByDescending(g => g.Max(c => c.Duration)),
            5 => groups.OrderByDescending(g => g.Max(c => c.FileSizeBytes)),
            _ => groups.OrderByDescending(g => g.Max(c => c.EffectiveDate))
        };

        var newGroups = new List<ClipGroup>();
        bool isFirst = true;
        foreach (var group in groups)
        {
            var firstClip = group.First();
            var dt = firstClip.EffectiveDate.ToLocalTime();
            string title;

            if (DateGrouping == DateGroupingMode.Day)
            {
                var diff = now - dt.Date;
                if (diff.TotalDays == 0)
                {
                    title = $"{todayStr} • {FormatGroupDate(dt, culture, includeDayOfWeek: false)}";
                }
                else if (diff.TotalDays == 1)
                {
                    title = $"{yesterdayStr} • {FormatGroupDate(dt, culture, includeDayOfWeek: false)}";
                }
                else
                {
                    title = FormatGroupDate(dt, culture, includeDayOfWeek: true);
                }
            }
            else if (DateGrouping == DateGroupingMode.Month)
            {
                title = FormatGroupMonth(dt, culture);
            }
            else
            {
                title = dt.ToString("yyyy", culture);
            }

            int count = group.Count();
            long totalBytes = group.Sum(c => c.FileSizeBytes);
            string sizeFormatted = FormatBytes(totalBytes, loc, culture);
            string pluralClip = loc != null ? loc.FormatPlural("Plural_Clip", count) : (count == 1 ? "clip" : "clips");
            string subtitle = $"{count} {pluralClip} • {sizeFormatted}";

            var clipGroup = new ClipGroup(group.Key, title, subtitle, group, isHeaderVisible: true)
            {
                ShowGroupDivider = !isFirst
            };
            isFirst = false;
            newGroups.Add(clipGroup);
        }

        GroupedClips.ReplaceRange(newGroups);
    }

    private static string FormatGroupDate(DateTime dt, System.Globalization.CultureInfo culture, bool includeDayOfWeek)
    {
        bool dayFirst = culture.DateTimeFormat.LongDatePattern.IndexOf('d') < culture.DateTimeFormat.LongDatePattern.IndexOf('M');
        string pattern;
        if (includeDayOfWeek)
        {
            pattern = dayFirst ? "dddd, d MMMM yyyy" : "dddd, MMMM d, yyyy";
        }
        else
        {
            pattern = dayFirst ? "d MMMM yyyy" : "MMMM d, yyyy";
        }

        string result = dt.ToString(pattern, culture);
        if (!string.IsNullOrEmpty(result) && char.IsLower(result[0]))
        {
            result = char.ToUpper(result[0], culture) + result.Substring(1);
        }
        return result;
    }

    private static string FormatGroupMonth(DateTime dt, System.Globalization.CultureInfo culture)
    {
        string result = dt.ToString("MMMM yyyy", culture);
        if (!string.IsNullOrEmpty(result) && char.IsLower(result[0]))
        {
            result = char.ToUpper(result[0], culture) + result.Substring(1);
        }
        return result;
    }

    private static string FormatBytes(long total, ILocalizationService? loc, System.Globalization.CultureInfo culture)
    {
        string bUnit = loc?["Unit_Byte"] ?? "B";
        string kbUnit = loc?["Unit_KB"] ?? "KB";
        string mbUnit = loc?["Unit_MB"] ?? "MB";
        string gbUnit = loc?["Unit_GB"] ?? "GB";

        if (total < 1024) return string.Format(culture, "{0} {1}", total, bUnit);
        double mb = total / (1024.0 * 1024.0);
        if (mb < 1024) return string.Format(culture, "{0:F1} {1}", mb, mbUnit);
        double gb = mb / 1024.0;
        return string.Format(culture, "{0:F2} {1}", gb, gbUnit);
    }

    private void OnClipAdded(string filePath)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            // A full rescan would be skipped (watch folders unchanged), so add the new clip directly
            if (_syncTask != null)
            {
                _ = SyncDirectoriesAsync(forceReload: true);
                return;
            }
            if (FindClip(filePath) != null) return;
            if (!_loadedWatchDirectories.Any(root => PathUtil.IsUnder(filePath, root))) return;

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return;
            }
            catch
            {
                return;
            }

            var clip = GameClip.FromFileInfo(fileInfo);
            ApplyPersistedState(clip, _storageService.CurrentSettings);
            AllClips.Insert(0, clip);
            AdjustFolderCounts(filePath, +1);

            if (ClipBelongsToCurrentView(clip))
            {
                Clips.Insert(0, clip);
                ApplyFilterAndSort();
            }
            UpdateCounts();

            _ = Task.Run(() => _metadataService.PopulateMetadataAsync(clip));
        });
    }

    private void OnClipDeleted(string filePath)
    {
        // External deletes keep favorites/moments in settings, so a file restored from the Recycle Bin gets them back
        _dispatcherQueue.TryEnqueue(() => RemoveClipFromLibrary(filePath));
    }

    private void OnClipRenamed(string oldPath, string newPath)
    {
        _dispatcherQueue.TryEnqueue(() => ApplyRename(oldPath, newPath));
    }

    /// <summary>
    /// Updates <paramref name="target"/> in place to match <paramref name="source"/>, keeping existing
    /// node instances (and so their expanded state, selection and tree containers) wherever the path matches.
    /// </summary>
    private static void MergeDirectoryNodes(ObservableCollection<DirectoryNode> target, IList<DirectoryNode> source)
    {
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!source.Any(s => PathUtil.PathEquals(s.FullPath, target[i].FullPath)))
            {
                target.RemoveAt(i);
            }
        }

        for (int i = 0; i < source.Count; i++)
        {
            var incoming = source[i];
            int existingIndex = -1;
            for (int j = 0; j < target.Count; j++)
            {
                if (PathUtil.PathEquals(target[j].FullPath, incoming.FullPath))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                target.Insert(Math.Min(i, target.Count), incoming);
                continue;
            }

            if (existingIndex != i && i < target.Count)
            {
                target.Move(existingIndex, i);
            }

            var node = target[i];
            if (node.ClipCount != incoming.ClipCount) node.ClipCount = incoming.ClipCount;
            if (node.IsWatchRoot != incoming.IsWatchRoot) node.IsWatchRoot = incoming.IsWatchRoot;
            if (node.Name != incoming.Name) node.Name = incoming.Name;
            MergeDirectoryNodes(node.SubDirectories, incoming.SubDirectories);
            node.HasSubDirectories = node.SubDirectories.Count > 0;
        }
    }

    public static DirectoryNode? FindNodeByPath(IEnumerable<DirectoryNode> nodes, string path)
    {
        foreach (var n in nodes)
        {
            if (PathUtil.PathEquals(n.FullPath, path))
                return n;

            var sub = FindNodeByPath(n.SubDirectories, path);
            if (sub != null)
                return sub;
        }
        return null;
    }
}
