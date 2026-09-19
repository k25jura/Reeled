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
    private readonly ILocalizationService _localizationService;
    private readonly HomeViewModel _homeViewModel;

    public ILocalizationService Loc => _localizationService;
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
    private string _momentsStatsFormatted = "Calculating...";

    [ObservableProperty]
    private bool _enableSkeletonLoading = true;

    [ObservableProperty]
    private double _defaultPlaybackSpeed = 1.0;

    [ObservableProperty]
    private bool _rememberPlaybackSpeed = true;

    [ObservableProperty]
    private int _selectedThemeIndex = 0;

    [ObservableProperty]
    private int _selectedLanguageIndex = 0;

    [ObservableProperty]
    private string _sampleDateFormatPreview = string.Empty;

    // Updates properties
    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    private bool _isUpdateReadyToInstall;

    public bool CanDownloadUpdate => !IsDownloadingUpdate && !IsUpdateReadyToInstall;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadProgressFormatted = string.Empty;

    [ObservableProperty]
    private string _downloadProgressDetailed = string.Empty;

    [ObservableProperty]
    private string _updatesStatusMessage = string.Empty;

    [ObservableProperty]
    private string _updateStatusFormatted = string.Empty;

    [ObservableProperty]
    private string _lastCheckedFormatted = string.Empty;

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    public SettingsViewModel(
        ILocalStorageService storageService,
        IClipIndexerService indexerService,
        INavigationService navigationService,
        IClipMetadataCacheService clipMetadataCacheService,
        IThumbnailService thumbnailService,
        ILocalizationService localizationService,
        HomeViewModel homeViewModel)
    {
        _storageService = storageService;
        _indexerService = indexerService;
        _navigationService = navigationService;
        _clipMetadataCacheService = clipMetadataCacheService;
        _thumbnailService = thumbnailService;
        _localizationService = localizationService;
        _homeViewModel = homeViewModel;

        _localizationService.LanguageChanged += (s, e) =>
        {
            UpdateFormattedStrings();
        };
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
        SelectedLanguageIndex = _storageService.CurrentSettings.Language switch
        {
            "en" => 1,
            "uk" => 2,
            _ => 0
        };
        AutoCheckUpdates = _storageService.CurrentSettings.AutoCheckUpdates;
        UpdateStatusFormatted = _localizationService["Updates_StatusTitle"];
        LastCheckedFormatted = string.Format(_localizationService["Updates_LastChecked"], _localizationService["Date_Today"]);

        CalculateCacheSize();
        CalculateClipCacheSize();
        CalculateMomentsStats();
        UpdateFormattedStrings();
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

    public void SetLanguage(int index)
    {
        SelectedLanguageIndex = index;
        string langCode = index switch
        {
            1 => "en",
            2 => "uk",
            _ => "System"
        };
        var settings = _storageService.CurrentSettings;
        settings.Language = langCode;
        _ = _storageService.SaveSettingsAsync(settings);
        _localizationService.SetLanguage(langCode);
        UpdateFormattedStrings();
    }

    public void UpdateFormattedStrings()
    {
        SampleDateFormatPreview = DateTime.Now.ToString("dddd, d MMMM yyyy, HH:mm", _localizationService.CurrentCulture);
        CalculateMomentsStats();
        if (IsUpdateAvailable)
        {
            UpdateStatusFormatted = _localizationService["Updates_StatusAvailable"];
        }
        else if (IsUpdateReadyToInstall)
        {
            UpdateStatusFormatted = _localizationService["Updates_StatusReady"];
        }
        else
        {
            UpdateStatusFormatted = _localizationService["Updates_StatusTitle"];
        }
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

    [RelayCommand]
    public async Task ClearMomentsMetadataAsync()
    {
        try
        {
            int totalMoments = 0;
            foreach (var kv in _storageService.CurrentSettings.Bookmarks)
            {
                totalMoments += kv.Value.Count;
            }

            var settings = _storageService.CurrentSettings;
            settings.Bookmarks.Clear();
            await _storageService.SaveSettingsAsync(settings);

            foreach (var clip in _homeViewModel.AllClips)
            {
                clip.Bookmarks.Clear();
                clip.RefreshFormattedStrings();
            }
            _homeViewModel.RefreshSavedMoments();
            CalculateMomentsStats();

            StorageStatusMessage = totalMoments > 0
                ? string.Format(_localizationService["Storage_Status_MomentsCleared"], totalMoments)
                : _localizationService["Storage_Status_NoMoments"];
            StatusMessage = StorageStatusMessage;
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Error: {ex.Message}";
            StatusMessage = StorageStatusMessage;
        }
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates || IsDownloadingUpdate) return;

        IsCheckingForUpdates = true;
        IsUpdateAvailable = false;
        IsDownloadingUpdate = false;
        IsUpdateReadyToInstall = false;
        UpdateStatusFormatted = _localizationService["Updates_StatusChecking"];

        // Simulate Windows Update search latency (1.5s)
        await Task.Delay(1500);

        IsCheckingForUpdates = false;
        IsUpdateAvailable = true;
        UpdateStatusFormatted = _localizationService["Updates_StatusAvailable"];
        LastCheckedFormatted = string.Format(_localizationService["Updates_LastChecked"], DateTime.Now.ToString("t", _localizationService.CurrentCulture));
    }

    [RelayCommand]
    public async Task DownloadUpdateAsync()
    {
        if (IsDownloadingUpdate) return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        DownloadProgressFormatted = $"{_localizationService["Updates_StatusDownloading"]} 0%";
        DownloadProgressDetailed = string.Format(_localizationService["Updates_Progress_Format"], "0.0", "38.4", 0, "4.8");

        for (int p = 0; p <= 100; p += 5)
        {
            await Task.Delay(80);
            DownloadProgress = p;
            double downloadedMb = (p / 100.0) * 38.4;
            double speed = 4.8 + (p % 3) * 0.3;
            DownloadProgressFormatted = $"{_localizationService["Updates_StatusDownloading"]} {p}%";
            DownloadProgressDetailed = string.Format(_localizationService["Updates_Progress_Format"], $"{downloadedMb:F1}", "38.4", p, $"{speed:F1}");
        }

        IsDownloadingUpdate = false;
        IsUpdateReadyToInstall = true;
        UpdateStatusFormatted = _localizationService["Updates_StatusReady"];
    }

    [RelayCommand]
    public void InstallUpdate()
    {
        IsUpdateReadyToInstall = false;
        IsUpdateAvailable = false;
        UpdateStatusFormatted = _localizationService["Updates_StatusTitle"];
        UpdatesStatusMessage = _localizationService["Updates_Status_Installed"];
        StatusMessage = UpdatesStatusMessage;
    }

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        settings.AutoCheckUpdates = value;
        _ = _storageService.SaveSettingsAsync(settings);
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

    private void CalculateMomentsStats()
    {
        try
        {
            int totalMoments = 0;
            int clipsWithMoments = 0;
            foreach (var kv in _storageService.CurrentSettings.Bookmarks)
            {
                if (kv.Value.Count > 0)
                {
                    totalMoments += kv.Value.Count;
                    clipsWithMoments++;
                }
            }

            if (totalMoments == 0)
            {
                MomentsStatsFormatted = _localizationService.EffectiveLanguage == "uk" 
                    ? "0 збережених моментів" 
                    : "0 moments saved";
            }
            else
            {
                string mPlural = _localizationService.FormatPlural("Plural_Moment", totalMoments);
                string cPlural = _localizationService.FormatPlural("Plural_Clip", clipsWithMoments);
                MomentsStatsFormatted = _localizationService.EffectiveLanguage == "uk"
                    ? $"{totalMoments} {mPlural} у {clipsWithMoments} {cPlural}"
                    : $"{totalMoments} {mPlural} across {clipsWithMoments} {cPlural}";
            }
        }
        catch
        {
            MomentsStatsFormatted = "0 moments saved";
        }
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
