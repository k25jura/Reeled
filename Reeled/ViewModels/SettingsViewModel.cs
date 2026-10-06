using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage.Pickers;
using Reeled.Models;
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
    private readonly IUpdateService _updateService;

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
    [NotifyPropertyChangedFor(nameof(VolumeFormatted))]
    private int _volume = 100;

    public string VolumeFormatted => $"{Volume}%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxVolumeFormatted))]
    private int _maxVolume = 200;

    public string MaxVolumeFormatted => $"{MaxVolume}%";

    [ObservableProperty]
    private bool _startMuted;

    [ObservableProperty]
    private int _selectedRepeatModeIndex = 0;

    [ObservableProperty]
    private int _selectedAutoHideIndex = 0;

    [ObservableProperty]
    private bool _autoHideCursor = true;

    [ObservableProperty]
    private bool _enableOsdNotifications = true;

    [ObservableProperty]
    private bool _enableClipCache = true;

    [ObservableProperty]
    private bool _showMomentsBadges = true;

    [ObservableProperty]
    private bool _enableBackdropBlur = true;

    [ObservableProperty]
    private int _selectedThemeIndex = 0;

    [ObservableProperty]
    private int _selectedLanguageIndex = 0;

    [ObservableProperty]
    private string _sampleDateFormatPreview = string.Empty;

    // Diagnostics & About Hero
    [ObservableProperty]
    private string _osVersionFormatted = string.Empty;

    [ObservableProperty]
    private string _architectureFormatted = string.Empty;

    [ObservableProperty]
    private string _runtimeFormatted = string.Empty;

    [ObservableProperty]
    private string _memoryUsageFormatted = string.Empty;

    [ObservableProperty]
    private string _libraryStatsFormatted = string.Empty;

    [ObservableProperty]
    private string _aboutStatusMessage = string.Empty;

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

    [ObservableProperty]
    private bool _reduceMotion;

    [ObservableProperty]
    private string _gitHubToken = string.Empty;

    [ObservableProperty]
    private string _updateAvailableVersion = string.Empty;

    [ObservableProperty]
    private string _updateAvailableNotes = string.Empty;

    [ObservableProperty]
    private string _updateReleaseUrl = string.Empty;

    [ObservableProperty]
    private string _updateAssetSizeFormatted = string.Empty;

    [ObservableProperty]
    private string _downloadedFilePath = string.Empty;

    private UpdateInfo? _currentUpdateInfo;

    public SettingsViewModel(
        ILocalStorageService storageService,
        IClipIndexerService indexerService,
        INavigationService navigationService,
        IClipMetadataCacheService clipMetadataCacheService,
        IThumbnailService thumbnailService,
        ILocalizationService localizationService,
        HomeViewModel homeViewModel,
        IUpdateService updateService)
    {
        _storageService = storageService;
        _indexerService = indexerService;
        _navigationService = navigationService;
        _clipMetadataCacheService = clipMetadataCacheService;
        _thumbnailService = thumbnailService;
        _localizationService = localizationService;
        _homeViewModel = homeViewModel;
        _updateService = updateService;

        _localizationService.LanguageChanged += (s, e) =>
        {
            UpdateFormattedStrings();
        };

        Initialize();
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
        Volume = _storageService.CurrentSettings.DefaultVolume;
        MaxVolume = _storageService.CurrentSettings.MaxVolume;
        StartMuted = _storageService.CurrentSettings.StartMuted;
        SelectedRepeatModeIndex = _storageService.CurrentSettings.DefaultRepeatMode switch
        {
            RepeatMode.One => 1,
            RepeatMode.All => 2,
            _ => 0
        };
        SelectedAutoHideIndex = _storageService.CurrentSettings.AutoHideControlsSeconds switch
        {
            3 => 1,
            5 => 2,
            0 => 3,
            _ => 0
        };
        AutoHideCursor = _storageService.CurrentSettings.AutoHideCursor;
        EnableOsdNotifications = _storageService.CurrentSettings.EnableOsdNotifications;
        EnableClipCache = _storageService.CurrentSettings.EnableClipCache;
        ShowMomentsBadges = _storageService.CurrentSettings.ShowMomentsBadges;
        EnableBackdropBlur = _storageService.CurrentSettings.EnableBackdropBlur;
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
        ReduceMotion = _storageService.CurrentSettings.ReduceMotion;
        GitHubToken = _storageService.CurrentSettings.GitHubToken ?? string.Empty;
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
        CalculateClipCacheSize();
        UpdateDiagnostics();
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
        settings.PlaybackSpeed = speed;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    partial void OnRememberPlaybackSpeedChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        settings.RememberPlaybackSpeed = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        var settings = _storageService.CurrentSettings;
        if (settings.DefaultVolume != Volume)
        {
            settings.DefaultVolume = Volume;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    partial void OnVolumeChanged(int value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.DefaultVolume != value)
        {
            settings.DefaultVolume = value;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    public void SetMaxVolume(int volume)
    {
        int clamped = Math.Clamp(volume, 100, 300);
        if (MaxVolume != clamped)
        {
            MaxVolume = clamped;
        }
    }

    partial void OnMaxVolumeChanged(int value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.MaxVolume != value)
        {
            settings.MaxVolume = value;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    partial void OnStartMutedChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.StartMuted != value)
        {
            settings.StartMuted = value;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    public void SetRepeatMode(int index)
    {
        SelectedRepeatModeIndex = index;
        var mode = index switch
        {
            1 => RepeatMode.One,
            2 => RepeatMode.All,
            _ => RepeatMode.Off
        };
        var settings = _storageService.CurrentSettings;
        if (settings.DefaultRepeatMode != mode)
        {
            settings.DefaultRepeatMode = mode;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    public void SetAutoHideTimeout(int index)
    {
        SelectedAutoHideIndex = index;
        int seconds = index switch
        {
            1 => 3,
            2 => 5,
            3 => 0,
            _ => 2
        };
        var settings = _storageService.CurrentSettings;
        if (settings.AutoHideControlsSeconds != seconds)
        {
            settings.AutoHideControlsSeconds = seconds;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    partial void OnEnableOsdNotificationsChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.EnableOsdNotifications != value)
        {
            settings.EnableOsdNotifications = value;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    partial void OnEnableClipCacheChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.EnableClipCache != value)
        {
            settings.EnableClipCache = value;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    partial void OnShowMomentsBadgesChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.ShowMomentsBadges != value)
        {
            settings.ShowMomentsBadges = value;
            _ = _storageService.SaveSettingsAsync(settings);
            _homeViewModel.ShowMomentsBadges = value;
        }
    }

    partial void OnEnableBackdropBlurChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.EnableBackdropBlur != value)
        {
            settings.EnableBackdropBlur = value;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    partial void OnAutoHideCursorChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.AutoHideCursor != value)
        {
            settings.AutoHideCursor = value;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    public void UpdateDiagnostics()
    {
        try
        {
            var os = Environment.OSVersion;
            string osName = os.Platform == PlatformID.Win32NT ? $"Windows (Build {os.Version.Build})" : os.VersionString;
            OsVersionFormatted = osName;

            ArchitectureFormatted = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
            RuntimeFormatted = $".NET {Environment.Version.Major}.{Environment.Version.Minor}.{Environment.Version.Build}";

            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            double mb = proc.WorkingSet64 / (1024.0 * 1024.0);
            string mbUnit = _localizationService["Unit_MB"];
            MemoryUsageFormatted = string.Format(_localizationService.CurrentCulture, "{0:F1} {1}", mb, mbUnit);

            int folderCount = WatchFolders.Count;
            int clipCount = _homeViewModel.AllClips.Count;
            string folderPlural = _localizationService.FormatPlural("Plural_Folder", folderCount);
            string clipPlural = _localizationService.FormatPlural("Plural_Clip", clipCount);
            LibraryStatsFormatted = $"{folderCount} {folderPlural}, {clipCount} {clipPlural}";
        }
        catch
        {
            OsVersionFormatted = "Windows (x64)";
            ArchitectureFormatted = "X64";
            RuntimeFormatted = ".NET 8.0";
            MemoryUsageFormatted = "Normal";
            LibraryStatsFormatted = $"{WatchFolders.Count} folders, {_homeViewModel.AllClips.Count} clips";
        }
    }

    [RelayCommand]
    public Task CopyDiagnosticsAsync()
    {
        try
        {
            UpdateDiagnostics();
            var text = "--- Reeled Diagnostics ---\n" +
                       $"Version: 1.0.0\n" +
                       $"OS: {OsVersionFormatted}\n" +
                       $"Architecture: {ArchitectureFormatted}\n" +
                       $"Runtime: {RuntimeFormatted}\n" +
                       $"Working Set Memory: {MemoryUsageFormatted}\n" +
                       $"Library: {LibraryStatsFormatted}\n" +
                       $"Theme: {_storageService.CurrentSettings.AppTheme}\n" +
                       $"Language: {_localizationService.CurrentLanguage} (Effective: {_localizationService.EffectiveLanguage})\n" +
                       "--------------------------";

            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

            AboutStatusMessage = _localizationService["About_DiagnosticsCopied"];
        }
        catch (Exception ex)
        {
            AboutStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
        }
        return Task.CompletedTask;
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
                FolderStatusMessage = string.Format(_localizationService["Folders_Status_Added"], folder.Path);
                StatusMessage = FolderStatusMessage;
            }
        }
        catch (Exception ex)
        {
            FolderStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
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
            FolderStatusMessage = string.Format(_localizationService["Folders_Status_Removed"], folder);
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
            string mbUnit = _localizationService["Unit_MB"];
            StorageStatusMessage = deletedCount > 0
                ? string.Format(_localizationService.CurrentCulture, _localizationService["Storage_Status_ThumbnailsCleared"], deletedCount, freedMb, mbUnit)
                : _localizationService["Storage_Status_ThumbnailsEmpty"];
            StatusMessage = StorageStatusMessage;
        }
        catch (Exception ex)
        {
            StorageStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
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
            StorageStatusMessage = _localizationService["Storage_Status_ClipMetadataCleared"];
            StatusMessage = StorageStatusMessage;
        }
        catch (Exception ex)
        {
            StorageStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
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
            StorageStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
            StatusMessage = StorageStatusMessage;
        }
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates || IsDownloadingUpdate) return;

        IsCheckingForUpdates = true;
        UpdateStatusFormatted = _localizationService["Updates_StatusChecking"];
        IsUpdateAvailable = false;
        IsDownloadingUpdate = false;
        IsUpdateReadyToInstall = false;

        try
        {
            var info = await _updateService.CheckForUpdatesAsync(GitHubToken);
            _currentUpdateInfo = info;

            if (info.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                UpdateAvailableVersion = !string.IsNullOrEmpty(info.LatestVersionTag) ? info.LatestVersionTag : $"v{info.LatestVersion}";
                UpdateAvailableNotes = !string.IsNullOrWhiteSpace(info.ReleaseNotes)
                    ? info.ReleaseNotes
                    : (!string.IsNullOrWhiteSpace(info.ReleaseName) ? info.ReleaseName : _localizationService["Updates_AvailableNotes"]);
                UpdateReleaseUrl = info.ReleaseHtmlUrl;
                UpdateAssetSizeFormatted = info.AssetSizeFormatted;
                UpdateStatusFormatted = _localizationService["Updates_StatusAvailable"];
                UpdatesStatusMessage = string.Format(_localizationService["Updates_StatusAvailable"]);
            }
            else
            {
                IsUpdateAvailable = false;
                if (!string.IsNullOrEmpty(info.ErrorMessage))
                {
                    UpdateStatusFormatted = _localizationService["Updates_StatusTitle"];
                    UpdatesStatusMessage = string.Format(_localizationService["Updates_ErrorCheck"], info.ErrorMessage);
                }
                else
                {
                    UpdateStatusFormatted = _localizationService["Updates_StatusTitle"];
                    UpdatesStatusMessage = string.Format(_localizationService["Updates_NoUpdates"], info.CurrentVersion);
                }
            }

            LastCheckedFormatted = string.Format(_localizationService["Updates_LastChecked"], DateTime.Now.ToString("t", _localizationService.CurrentCulture));
        }
        catch (Exception ex)
        {
            IsUpdateAvailable = false;
            UpdateStatusFormatted = _localizationService["Updates_StatusTitle"];
            UpdatesStatusMessage = string.Format(_localizationService["Updates_ErrorCheck"], ex.Message);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public async Task DownloadUpdateAsync()
    {
        if (IsDownloadingUpdate || _currentUpdateInfo == null) return;

        if (string.IsNullOrWhiteSpace(_currentUpdateInfo.AssetDownloadUrl))
        {
            if (!string.IsNullOrWhiteSpace(_currentUpdateInfo.ReleaseHtmlUrl))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(_currentUpdateInfo.ReleaseHtmlUrl));
            }
            return;
        }

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        DownloadProgressFormatted = $"{_localizationService["Updates_StatusDownloading"]} 0%";

        var progress = new Progress<DownloadProgressReport>(report =>
        {
            DownloadProgress = report.Percent;
            DownloadProgressFormatted = $"{_localizationService["Updates_StatusDownloading"]} {(int)report.Percent}%";
            DownloadProgressDetailed = string.Format(_localizationService["Updates_Progress_Format"], report.DownloadedMbFormatted, report.TotalMbFormatted, (int)report.Percent, report.SpeedFormatted);
        });

        try
        {
            DownloadedFilePath = await _updateService.DownloadUpdateAssetAsync(_currentUpdateInfo, progress);
            IsDownloadingUpdate = false;
            IsUpdateReadyToInstall = true;
            UpdateStatusFormatted = _localizationService["Updates_StatusReady"];
        }
        catch (Exception ex)
        {
            IsDownloadingUpdate = false;
            UpdatesStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
            UpdateStatusFormatted = _localizationService["Updates_StatusAvailable"];
        }
    }

    [RelayCommand]
    public async Task InstallUpdateAsync()
    {
        if (string.IsNullOrEmpty(DownloadedFilePath) || !File.Exists(DownloadedFilePath))
        {
            if (!string.IsNullOrEmpty(_currentUpdateInfo?.ReleaseHtmlUrl))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(_currentUpdateInfo.ReleaseHtmlUrl));
            }
            return;
        }

        try
        {
            await _updateService.LaunchInstallerAsync(DownloadedFilePath);
            UpdatesStatusMessage = _localizationService["Updates_Status_Installed"];
        }
        catch (Exception ex)
        {
            UpdatesStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
        }
    }

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        settings.AutoCheckUpdates = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    partial void OnReduceMotionChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        settings.ReduceMotion = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    partial void OnGitHubTokenChanged(string value)
    {
        var settings = _storageService.CurrentSettings;
        settings.GitHubToken = value;
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
            string mbUnit = _localizationService["Unit_MB"];
            CacheSizeFormatted = string.Format(_localizationService.CurrentCulture, "{0:F1} {1}", mb, mbUnit);
        }
        catch
        {
            string kbUnit = _localizationService["Unit_KB"];
            CacheSizeFormatted = string.Format(_localizationService.CurrentCulture, "0 {0}", kbUnit);
        }
    }

    private void CalculateClipCacheSize()
    {
        string kbUnit = _localizationService["Unit_KB"];
        string mbUnit = _localizationService["Unit_MB"];
        try
        {
            var (count, bytes) = _clipMetadataCacheService.GetCacheStats();
            double kb = bytes / 1024.0;
            string clipPlural = _localizationService.FormatPlural("Plural_Clip", count);
            string sizeStr = kb < 1024 
                ? string.Format(_localizationService.CurrentCulture, "{0:F1} {1}", kb, kbUnit)
                : string.Format(_localizationService.CurrentCulture, "{0:F2} {1}", kb / 1024.0, mbUnit);
            ClipCacheSizeFormatted = string.Format(_localizationService["Storage_ClipCacheStats"], count, clipPlural, sizeStr);
        }
        catch
        {
            string zeroKb = string.Format(_localizationService.CurrentCulture, "0 {0}", kbUnit);
            ClipCacheSizeFormatted = string.Format(_localizationService["Storage_ClipCacheStats"], 0, _localizationService.FormatPlural("Plural_Clip", 0), zeroKb);
        }
    }

    public async Task ResetOptionsToDefaultAsync()
    {
        var settings = _storageService.CurrentSettings;

        settings.DefaultVolume = 100;
        settings.Volume = 100;
        settings.MaxVolume = 200;
        settings.StartMuted = false;
        settings.IsMuted = false;
        settings.DefaultPlaybackSpeed = 1.0;
        settings.PlaybackSpeed = 1.0;
        settings.RememberPlaybackSpeed = true;
        settings.DefaultRepeatMode = RepeatMode.Off;
        settings.RepeatMode = RepeatMode.Off;
        settings.AutoHideControlsSeconds = 2;
        settings.AutoHideCursor = true;
        settings.EnableOsdNotifications = true;

        settings.AppTheme = "Default";
        settings.ShowMomentsBadges = true;
        settings.EnableBackdropBlur = true;
        settings.ViewDensity = ViewDensityMode.Comfortable;
        settings.DateGrouping = DateGroupingMode.None;
        settings.MetadataHoverOnly = false;
        settings.SortIndex = 0;
        settings.ReduceMotion = false;

        settings.EnableSkeletonLoading = true;
        settings.EnableClipCache = true;
        settings.AutoCheckUpdates = true;

        settings.Language = "System";

        await _storageService.SaveSettingsAsync(settings);

        App.ApplyTheme("Default");
        _localizationService.SetLanguage("System");
        Initialize();
        UpdateFormattedStrings();
    }
}
