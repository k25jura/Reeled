using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage.Pickers;
using Reeled.Services;

namespace Reeled.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILocalStorageService _storageService;
    private readonly IClipIndexerService _indexerService;
    private readonly INavigationService _navigationService;
    private readonly IClipMetadataCacheService _clipMetadataCacheService;
    private readonly IThumbnailService _thumbnailService;
    private readonly HomeViewModel _homeViewModel;

    public ObservableCollection<string> WatchFolders { get; } = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _folderStatusMessage = string.Empty;

    [ObservableProperty]
    private string _storageStatusMessage = string.Empty;

    [ObservableProperty]
    private string _cacheSizeFormatted = "Calculating...";

    [ObservableProperty]
    private string _clipCacheSizeFormatted = "Calculating...";

    [ObservableProperty]
    private bool _enableSkeletonLoading = true;

    [ObservableProperty]
    private double _defaultPlaybackSpeed = 1.0;

    [ObservableProperty]
    private bool _rememberPlaybackSpeed = true;

    [ObservableProperty]
    private int _selectedThemeIndex = 0;

    public SettingsViewModel(
        ILocalStorageService storageService,
        IClipIndexerService indexerService,
        INavigationService navigationService,
        IClipMetadataCacheService clipMetadataCacheService,
        IThumbnailService thumbnailService,
        HomeViewModel homeViewModel)
    {
        _storageService = storageService;
        _indexerService = indexerService;
        _navigationService = navigationService;
        _clipMetadataCacheService = clipMetadataCacheService;
        _thumbnailService = thumbnailService;
        _homeViewModel = homeViewModel;
    }

    public void Initialize()
    {
        WatchFolders.Clear();
        foreach (var folder in _storageService.CurrentSettings.WatchDirectories)
        {
            WatchFolders.Add(folder);
        }
        EnableSkeletonLoading = _storageService.CurrentSettings.EnableSkeletonLoading;
        DefaultPlaybackSpeed = _storageService.CurrentSettings.DefaultPlaybackSpeed;
        RememberPlaybackSpeed = _storageService.CurrentSettings.RememberPlaybackSpeed;
        SelectedThemeIndex = _storageService.CurrentSettings.AppTheme switch
        {
            "Dark" => 1,
            "Light" => 2,
            _ => 0
        };
        CalculateCacheSize();
        CalculateClipCacheSize();
    }

    public void SetAppTheme(int index)
    {
        SelectedThemeIndex = index;
        string themeStr = index switch
        {
            1 => "Dark",
            2 => "Light",
            _ => "Default"
        };
        var settings = _storageService.CurrentSettings;
        settings.AppTheme = themeStr;
        _ = _storageService.SaveSettingsAsync(settings);
        App.ApplyTheme(themeStr);
    }

    public void SetDefaultPlaybackSpeed(double speed)
    {
        DefaultPlaybackSpeed = speed;
        var settings = _storageService.CurrentSettings;
        settings.DefaultPlaybackSpeed = speed;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    partial void OnRememberPlaybackSpeedChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        settings.RememberPlaybackSpeed = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public async Task AddFolderAsync(IntPtr hwnd)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null && !WatchFolders.Contains(folder.Path))
            {
                WatchFolders.Add(folder.Path);
                var settings = _storageService.CurrentSettings;
                settings.WatchDirectories.Add(folder.Path);
                await _storageService.SaveSettingsAsync(settings);
                _indexerService.UpdateWatchers(settings.WatchDirectories);
                _ = _homeViewModel.SyncDirectoriesAsync(forceReload: true);
                FolderStatusMessage = $"Added {folder.Path}";
                StatusMessage = FolderStatusMessage;
            }
        }
        catch (Exception ex)
        {
            FolderStatusMessage = $"Error: {ex.Message}";
            StatusMessage = FolderStatusMessage;
        }
    }

    [RelayCommand]
    public async Task RemoveFolderAsync(string folder)
    {
        if (WatchFolders.Remove(folder))
        {
            var settings = _storageService.CurrentSettings;
            settings.WatchDirectories.Remove(folder);
            await _storageService.SaveSettingsAsync(settings);
            _indexerService.UpdateWatchers(settings.WatchDirectories);
            _ = _homeViewModel.SyncDirectoriesAsync(forceReload: true);
            FolderStatusMessage = $"Removed {folder}";
            StatusMessage = FolderStatusMessage;

            if (_homeViewModel.CurrentDirectoryPath != null &&
                (_homeViewModel.CurrentDirectoryPath.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                 _homeViewModel.CurrentDirectoryPath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                await _homeViewModel.SelectHomeAsync();
            }
        }
    }

    [RelayCommand]
    public async Task NavigateToDirectoryAsync(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        await _homeViewModel.SelectDirectoryByPathAsync(folderPath);
        _navigationService.NavigateToHome();
    }

    [RelayCommand]
    public void ClearCache()
    {
        try
        {
            _thumbnailService.ClearMemoryCache();
            _homeViewModel.ClearThumbnailsInMemory();
            int deletedCount = 0;
            long freedBytes = 0;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cacheDir = Path.Combine(localAppData, "Reeled", "Thumbnails");
            if (Directory.Exists(cacheDir))
            {
                foreach (var file in Directory.EnumerateFiles(cacheDir))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        long len = fi.Length;
                        File.Delete(file);
                        deletedCount++;
                        freedBytes += len;
                    }
                    catch { }
                }
            }
            CalculateCacheSize();
            double freedMb = freedBytes / (1024.0 * 1024.0);
            StorageStatusMessage = deletedCount > 0
                ? $"Thumbnail cache cleared ({deletedCount} files, {freedMb:F1} MB freed)"
                : "Thumbnail cache is already empty";
            StatusMessage = StorageStatusMessage;
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Error: {ex.Message}";
            StatusMessage = StorageStatusMessage;
        }
    }

    [RelayCommand]
    public async Task ClearClipCacheAsync()
    {
        try
        {
            await _clipMetadataCacheService.ClearAsync();
            CalculateClipCacheSize();
            StorageStatusMessage = "Clip metadata cache cleared";
            StatusMessage = StorageStatusMessage;
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Error: {ex.Message}";
            StatusMessage = StorageStatusMessage;
        }
    }

    async partial void OnEnableSkeletonLoadingChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.EnableSkeletonLoading != value)
        {
            settings.EnableSkeletonLoading = value;
            await _storageService.SaveSettingsAsync(settings);
            _homeViewModel.EnableSkeletonLoading = value;
        }
    }

    [RelayCommand]
    public void BackToHome()
    {
        _navigationService.NavigateToHome();
    }

    private void CalculateCacheSize()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cacheDir = Path.Combine(localAppData, "Reeled", "Thumbnails");
            if (!Directory.Exists(cacheDir))
            {
                CacheSizeFormatted = "0 KB";
                return;
            }

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(cacheDir))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }

            double mb = total / (1024.0 * 1024.0);
            CacheSizeFormatted = $"{mb:F1} MB";
        }
        catch
        {
            CacheSizeFormatted = "0 KB";
        }
    }

    private void CalculateClipCacheSize()
    {
        try
        {
            var (count, bytes) = _clipMetadataCacheService.GetCacheStats();
            double kb = bytes / 1024.0;
            if (kb < 1024)
            {
                ClipCacheSizeFormatted = $"{count} {(count == 1 ? "clip" : "clips")} cached • {kb:F1} KB";
            }
            else
            {
                double mb = kb / 1024.0;
                ClipCacheSizeFormatted = $"{count} {(count == 1 ? "clip" : "clips")} cached • {mb:F2} MB";
            }
        }
        catch
        {
            ClipCacheSizeFormatted = "0 clips cached";
        }
    }
}
