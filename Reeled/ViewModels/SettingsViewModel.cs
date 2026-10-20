using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
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
    private bool _lookUpGameIcons = true;

    [ObservableProperty]
    private bool _alwaysShowTabs;

    [ObservableProperty]
    private bool _reopenTabs = true;

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
    private string _displayFormatted = string.Empty;

    [ObservableProperty]
    private string _videoEngineFormatted = string.Empty;

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

    /// <summary>Download finished; the package is being verified and unpacked.</summary>
    [ObservableProperty]
    private bool _isPreparingUpdate;

    public bool CanDownloadUpdate => !IsDownloadingUpdate && !IsUpdateReadyToInstall;

    /// <summary>Whether this copy can install updates itself (installed copies) or only download the setup.</summary>
    public bool CanSelfUpdate => _updateService.CanSelfUpdate;

    [ObservableProperty]
    private bool _autoDownloadUpdates = true;

    partial void OnAutoDownloadUpdatesChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.AutoDownloadUpdates == value) return;
        settings.AutoDownloadUpdates = value;
        _ = _storageService.SaveSettingsAsync(settings);
        if (value && IsUpdateAvailable && !IsUpdateReadyToInstall && !IsDownloadingUpdate)
        {
            _ = DownloadUpdateCoreAsync(silent: true);
        }
    }

    /// <summary>Raised when a background check finds a version the user hasn't been told about yet.</summary>
    public event EventHandler? UpdatePromptRequested;

    private StagedUpdate? _stagedUpdate;
    private bool _isApplyingUpdate;
    private bool _checkedThisSession;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _updateTimer;

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
    private bool _resumePlayback = true;

    [ObservableProperty]
    private bool _closeButtonReturnsToLibrary;

    [ObservableProperty]
    private int _backdropBlurLevel = 1;

    partial void OnBackdropBlurLevelChanged(int value)
    {
        var settings = _storageService.CurrentSettings;
        int clamped = Math.Clamp(value, 0, 2);
        if (settings.BackdropBlurLevel != clamped)
        {
            settings.BackdropBlurLevel = clamped;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

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
        SelectedSkipIntervalIndex = Math.Max(0, Array.IndexOf(SkipIntervals, _storageService.CurrentSettings.SkipSeconds));
        SelectedWheelActionIndex = (int)_storageService.CurrentSettings.PlayerWheelAction;
        HardwareDecoding = _storageService.CurrentSettings.HardwareDecoding;
        EnableOsdNotifications = _storageService.CurrentSettings.EnableOsdNotifications;
        EnableClipCache = _storageService.CurrentSettings.EnableClipCache;
        ShowMomentsBadges = _storageService.CurrentSettings.ShowMomentsBadges;
        LookUpGameIcons = _storageService.CurrentSettings.LookUpGameIcons;
        AlwaysShowTabs = _storageService.CurrentSettings.AlwaysShowTabs;
        ReopenTabs = _storageService.CurrentSettings.ReopenTabs;
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
        AutoDownloadUpdates = _storageService.CurrentSettings.AutoDownloadUpdates;
        ReduceMotion = _storageService.CurrentSettings.ReduceMotion;
        ResumePlayback = _storageService.CurrentSettings.ResumePlayback;
        CloseButtonReturnsToLibrary = _storageService.CurrentSettings.CloseButtonReturnsToLibrary;
        BackdropBlurLevel = _storageService.CurrentSettings.BackdropBlurLevel;
        GitHubToken = _storageService.CurrentSettings.GitHubToken ?? string.Empty;
        if (!IsUpdateAvailable && !IsUpdateReadyToInstall && !IsCheckingForUpdates)
        {
            UpdateStatusFormatted = _localizationService["Updates_StatusTitle"];
        }
        UpdateLastCheckedText();

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
        UpdateLastCheckedText();
        RefreshUpdateStatusText();
    }

    private void RefreshUpdateStatusText()
    {
        if (IsCheckingForUpdates)
        {
            UpdateStatusFormatted = _localizationService["Updates_StatusChecking"];
        }
        else if (IsUpdateReadyToInstall)
        {
            UpdateStatusFormatted = string.Format(_localizationService["Updates_StatusReadyFormat"], UpdateAvailableVersion);
        }
        else if (IsPreparingUpdate)
        {
            UpdateStatusFormatted = _localizationService["Updates_StatusPreparing"];
        }
        else if (IsDownloadingUpdate)
        {
            UpdateStatusFormatted = _localizationService["Updates_StatusDownloading"];
        }
        else if (IsUpdateAvailable)
        {
            UpdateStatusFormatted = string.Format(_localizationService["Updates_StatusAvailableFormat"], UpdateAvailableVersion);
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

    public static readonly int[] SkipIntervals = { 5, 10, 15, 30 };

    [ObservableProperty]
    private int _selectedSkipIntervalIndex;

    public void SetSkipInterval(int index)
    {
        SelectedSkipIntervalIndex = index;
        var settings = _storageService.CurrentSettings;
        int seconds = SkipIntervals[Math.Clamp(index, 0, SkipIntervals.Length - 1)];
        if (settings.SkipSeconds != seconds)
        {
            settings.SkipSeconds = seconds;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    [ObservableProperty]
    private int _selectedWheelActionIndex;

    public void SetWheelAction(int index)
    {
        SelectedWheelActionIndex = index;
        var settings = _storageService.CurrentSettings;
        var action = (WheelAction)Math.Clamp(index, 0, 2);
        if (settings.PlayerWheelAction != action)
        {
            settings.PlayerWheelAction = action;
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    [ObservableProperty]
    private bool _hardwareDecoding = true;

    partial void OnHardwareDecodingChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.HardwareDecoding == value) return;
        settings.HardwareDecoding = value;
        _ = _storageService.SaveSettingsAsync(settings);
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

    partial void OnLookUpGameIconsChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.LookUpGameIcons == value) return;
        settings.LookUpGameIcons = value;
        _ = _storageService.SaveSettingsAsync(settings);
        // Turning it on looks up the folders that have no icon yet; turning it off only stops new lookups
        if (value) _homeViewModel.RefreshGameIcons();
        else _homeViewModel.StopGameIconLookups();
    }

    /// <summary>The icon provider keys as stored (decrypted).</summary>
    public (string SteamGridDbKey, string IgdbClientId, string IgdbClientSecret) GetIconSources()
    {
        var settings = _storageService.CurrentSettings;
        return (GameIconCredentials.Unprotect(settings.ProtectedSteamGridDbKey) ?? string.Empty,
                settings.IgdbClientId ?? string.Empty,
                GameIconCredentials.Unprotect(settings.ProtectedIgdbClientSecret) ?? string.Empty);
    }

    /// <summary>
    /// Stores the icon provider keys (secrets encrypted). When they changed, folders that were looked up
    /// without luck are tried again.
    /// </summary>
    public void SetIconSources(string steamGridDbKey, string igdbClientId, string igdbClientSecret)
    {
        var (key, id, secret) = GetIconSources();
        steamGridDbKey = steamGridDbKey.Trim();
        igdbClientId = igdbClientId.Trim();
        igdbClientSecret = igdbClientSecret.Trim();
        if (steamGridDbKey == key && igdbClientId == id && igdbClientSecret == secret) return;

        var settings = _storageService.CurrentSettings;
        settings.ProtectedSteamGridDbKey = GameIconCredentials.Protect(steamGridDbKey);
        settings.IgdbClientId = string.IsNullOrEmpty(igdbClientId) ? null : igdbClientId;
        settings.ProtectedIgdbClientSecret = GameIconCredentials.Protect(igdbClientSecret);
        _ = _storageService.SaveSettingsAsync(settings);
        _homeViewModel.OnIconSourcesChanged();
    }

    partial void OnAlwaysShowTabsChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.AlwaysShowTabs == value) return;
        settings.AlwaysShowTabs = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    partial void OnReopenTabsChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.ReopenTabs == value) return;
        settings.ReopenTabs = value;
        // Turning it off forgets the saved tabs, turning it on saves the ones that are open now
        _homeViewModel.PersistTabs();
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

    [DllImport("psapi.dll", EntryPoint = "QueryWorkingSet", SetLastError = true)]
    private static extern bool QueryWorkingSet(IntPtr hProcess, IntPtr pv, uint cb);

    private static double GetActivePrivateWorkingSetMb(System.Diagnostics.Process proc)
    {
        try
        {
            // 4MB buffer can hold up to 524,288 page entries on 64-bit (~2GB of working set)
            int bufferSize = 1024 * 1024 * 4;
            IntPtr pBuf = Marshal.AllocHGlobal(bufferSize);
            try
            {
                if (QueryWorkingSet(proc.Handle, pBuf, (uint)bufferSize))
                {
                    long count = Marshal.ReadIntPtr(pBuf).ToInt64();
                    long privatePages = 0;
                    for (int i = 1; i <= count; i++)
                    {
                        long entry = Marshal.ReadIntPtr(pBuf, i * IntPtr.Size).ToInt64();
                        // Bit 8 is the Shared flag (0x100). If 0, the page is private to this process.
                        if ((entry & 0x100) == 0)
                        {
                            privatePages++;
                        }
                    }
                    return (privatePages * 4096.0) / (1024.0 * 1024.0);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pBuf);
            }
        }
        catch
        {
            // Fall back to PrivateMemorySize64 if QueryWorkingSet fails or access is denied
        }

        return proc.PrivateMemorySize64 / (1024.0 * 1024.0);
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
            double mb = GetActivePrivateWorkingSetMb(proc);
            string mbUnit = _localizationService["Unit_MB"];
            MemoryUsageFormatted = string.Format(_localizationService.CurrentCulture, "{0:F1} {1}", mb, mbUnit);

            int folderCount = WatchFolders.Count;
            int clipCount = _homeViewModel.AllClips.Count;
            string folderPlural = _localizationService.FormatPlural("Plural_Folder", folderCount);
            string clipPlural = _localizationService.FormatPlural("Plural_Clip", clipCount);
            LibraryStatsFormatted = $"{folderCount} {folderPlural}, {clipCount} {clipPlural} • {_homeViewModel.TotalStorageUsedFormatted}";

            DisplayFormatted = DescribeDisplay();
            string engine = App.GetService<ILibVlcPlaybackService>().EngineVersion is string v
                ? "LibVLC " + v.Split(' ')[0]
                : "LibVLC 3.0";
            bool hw = _storageService.CurrentSettings.HardwareDecoding;
            VideoEngineFormatted = $"{engine} • {(hw ? "D3D11 HW" : "Software")}";
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

    private static string DescribeDisplay()
    {
        try
        {
            if (App.Window is not MainWindow window) return string.Empty;
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(window.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            double scale = window.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            return $"{area.OuterBounds.Width}×{area.OuterBounds.Height} @ {scale * 100:0}%";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Plain-text environment report (copied from About, attached to feedback).</summary>
    public string BuildDiagnosticsReport(bool includeLogPath)
    {
        UpdateDiagnostics();
        var settings = _storageService.CurrentSettings;
        var uptime = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("--- Reeled Diagnostics ---");
        sb.AppendLine($"Version: {_updateService.CurrentVersion} ({(_updateService.CanSelfUpdate ? "installed" : "portable/dev")})");
        sb.AppendLine($"OS: {OsVersionFormatted} ({System.Runtime.InteropServices.RuntimeInformation.OSDescription})");
        sb.AppendLine($"Architecture: {ArchitectureFormatted}");
        sb.AppendLine($"Runtime: {RuntimeFormatted}");
        sb.AppendLine($"Windows App SDK: {typeof(Microsoft.UI.Xaml.Application).Assembly.GetName().Version}");
        sb.AppendLine($"Video engine: {VideoEngineFormatted}");
        sb.AppendLine($"Display: {DisplayFormatted}");
        sb.AppendLine($"Process Memory: {MemoryUsageFormatted}");
        sb.AppendLine($"Uptime: {(int)uptime.TotalHours}h {uptime.Minutes}m");
        sb.AppendLine($"Library: {LibraryStatsFormatted}");
        sb.AppendLine($"Theme: {settings.AppTheme}, Reduce motion: {settings.ReduceMotion}, Blur: {settings.EnableBackdropBlur} (level {settings.BackdropBlurLevel})");
        sb.AppendLine($"Playback: HW decoding {settings.HardwareDecoding}, skip {settings.SkipSeconds}s, wheel {settings.PlayerWheelAction}");
        sb.AppendLine($"Updates: auto-check {settings.AutoCheckUpdates}, auto-download {settings.AutoDownloadUpdates}");
        sb.AppendLine($"Language: {_localizationService.CurrentLanguage} (Effective: {_localizationService.EffectiveLanguage})");
        if (includeLogPath) sb.AppendLine($"Log: {Helpers.Log.LogFilePath}");
        sb.Append("--------------------------");
        return sb.ToString();
    }

    [RelayCommand]
    public Task CopyDiagnosticsAsync()
    {
        try
        {
            var text = BuildDiagnosticsReport(includeLogPath: true);

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
            if (folder != null && WatchFolders.Any(existing => Helpers.PathUtil.IsUnder(folder.Path, existing)))
            {
                // Already indexed through an existing watch folder (or added before): just open it
                await NavigateToDirectoryAsync(folder.Path);
            }
            else if (folder != null)
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

    /// <summary>Saves the current WatchFolders order (after a drag) and mirrors it in the library sidebar.</summary>
    public async Task SaveFolderOrderAsync()
    {
        var settings = _storageService.CurrentSettings;
        settings.WatchDirectories = new System.Collections.Generic.List<string>(WatchFolders);
        await _storageService.SaveSettingsAsync(settings);
        _homeViewModel.ApplyWatchFolderOrder();
    }

    /// <summary>Moves a watch folder in the list; the library sidebar follows the same order.</summary>
    public async Task MoveFolderAsync(string folder, int targetIndex)
    {
        int from = WatchFolders.IndexOf(folder);
        if (from < 0) return;
        targetIndex = Math.Clamp(targetIndex, 0, WatchFolders.Count - 1);
        if (from == targetIndex) return;

        WatchFolders.Move(from, targetIndex);
        var settings = _storageService.CurrentSettings;
        settings.WatchDirectories = new System.Collections.Generic.List<string>(WatchFolders);
        await _storageService.SaveSettingsAsync(settings);
        _homeViewModel.ApplyWatchFolderOrder();
    }

    public void RevealFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
        catch { }
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
    public Task CheckForUpdatesAsync() => CheckForUpdatesCoreAsync(silent: false);

    private async Task CheckForUpdatesCoreAsync(bool silent)
    {
        if (IsCheckingForUpdates || IsDownloadingUpdate || IsPreparingUpdate || _isApplyingUpdate) return;

        IsCheckingForUpdates = true;
        RefreshUpdateStatusText();

        try
        {
            var info = await _updateService.CheckForUpdatesAsync(GitHubToken);

            if (info.IsUpdateAvailable)
            {
                _currentUpdateInfo = info;
                string tag = !string.IsNullOrEmpty(info.LatestVersionTag) ? info.LatestVersionTag : $"v{info.LatestVersion}";
                bool isNewVersion = UpdateAvailableVersion != tag;
                UpdateAvailableVersion = tag;
                UpdateAvailableNotes = info.ReleaseNotes;
                UpdateReleaseUrl = info.ReleaseHtmlUrl;
                UpdateAssetSizeFormatted = info.AssetSizeFormatted;

                // A newer release than the one already staged replaces it
                if (_stagedUpdate != null && info.LatestVersion != null && info.LatestVersion > _stagedUpdate.Version)
                {
                    _stagedUpdate = null;
                    IsUpdateReadyToInstall = false;
                }
                IsUpdateAvailable = true;

                if (!silent)
                {
                    UpdatesStatusMessage = string.Format(_localizationService["Updates_StatusAvailableFormat"], tag);
                }

                if (silent && isNewVersion && !IsUpdatePromptDismissed(ready: false))
                {
                    UpdatePromptRequested?.Invoke(this, EventArgs.Empty);
                }

                if (silent && AutoDownloadUpdates && CanSelfUpdate && !IsUpdateReadyToInstall)
                {
                    _ = DownloadUpdateCoreAsync(silent: true);
                }
            }
            else if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                if (!silent)
                {
                    UpdatesStatusMessage = string.Format(_localizationService["Updates_ErrorCheck"], info.ErrorMessage);
                }
            }
            else if (!silent && !IsUpdateAvailable)
            {
                UpdatesStatusMessage = string.Format(_localizationService["Updates_NoUpdates"], info.CurrentVersion);
            }

            if (string.IsNullOrEmpty(info.ErrorMessage))
            {
                var settings = _storageService.CurrentSettings;
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                _ = _storageService.SaveSettingsAsync(settings);
            }
            UpdateLastCheckedText();
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                UpdatesStatusMessage = string.Format(_localizationService["Updates_ErrorCheck"], ex.Message);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
            RefreshUpdateStatusText();
        }
    }

    [RelayCommand]
    public Task DownloadUpdateAsync() => DownloadUpdateCoreAsync(silent: false);

    private async Task DownloadUpdateCoreAsync(bool silent)
    {
        if (IsDownloadingUpdate || IsPreparingUpdate || _currentUpdateInfo == null) return;

        bool selfUpdate = CanSelfUpdate && !string.IsNullOrWhiteSpace(_currentUpdateInfo.PackageDownloadUrl);
        if (!selfUpdate && string.IsNullOrWhiteSpace(_currentUpdateInfo.InstallerDownloadUrl))
        {
            if (!silent && !string.IsNullOrWhiteSpace(_currentUpdateInfo.ReleaseHtmlUrl))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(_currentUpdateInfo.ReleaseHtmlUrl));
            }
            return;
        }

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        DownloadProgressFormatted = $"{_localizationService["Updates_StatusDownloading"]} 0%";
        DownloadProgressDetailed = string.Empty;
        RefreshUpdateStatusText();

        var progress = new Progress<DownloadProgressReport>(report =>
        {
            if (report.IsPreparing)
            {
                IsPreparingUpdate = true;
                DownloadProgress = 100;
                DownloadProgressFormatted = _localizationService["Updates_StatusPreparing"];
                DownloadProgressDetailed = string.Empty;
                RefreshUpdateStatusText();
                return;
            }
            DownloadProgress = report.Percent;
            DownloadProgressFormatted = $"{_localizationService["Updates_StatusDownloading"]} {(int)report.Percent}%";
            DownloadProgressDetailed = string.Format(_localizationService["Updates_Progress_Format"], report.DownloadedMbFormatted, report.TotalMbFormatted, (int)report.Percent, report.SpeedFormatted);
        });

        try
        {
            if (selfUpdate)
            {
                _stagedUpdate = await _updateService.DownloadAndStageAsync(_currentUpdateInfo, progress);
            }
            else
            {
                DownloadedFilePath = await _updateService.DownloadInstallerAsync(_currentUpdateInfo, progress);
            }
            IsDownloadingUpdate = false;
            IsPreparingUpdate = false;
            IsUpdateReadyToInstall = true;
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("Update.Download", ex);
            IsDownloadingUpdate = false;
            IsPreparingUpdate = false;
            if (!silent)
            {
                UpdatesStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
            }
        }
        finally
        {
            RefreshUpdateStatusText();
        }
    }

    [RelayCommand]
    public async Task InstallUpdateAsync()
    {
        if (_stagedUpdate != null && CanSelfUpdate)
        {
            try
            {
                _isApplyingUpdate = true;
                _updateService.ApplyStagedUpdate(_stagedUpdate, restart: true);
                // The helper waits for this process to exit before swapping files
                App.Window?.Close();
            }
            catch (Exception ex)
            {
                // Declined elevation prompt or a missing helper: keep running
                _isApplyingUpdate = false;
                UpdatesStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
            }
            return;
        }

        if (!string.IsNullOrEmpty(DownloadedFilePath) && File.Exists(DownloadedFilePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DownloadedFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                UpdatesStatusMessage = string.Format(_localizationService["Common_Error_Format"], ex.Message);
            }
            return;
        }

        if (!string.IsNullOrEmpty(_currentUpdateInfo?.ReleaseHtmlUrl))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_currentUpdateInfo.ReleaseHtmlUrl));
        }
    }

    /// <summary>Section the Settings page should open on next (e.g. "UpdateSection" from the update prompt).</summary>
    public string? PendingSection { get; set; }

    public string? ConsumePendingSection()
    {
        var section = PendingSection;
        PendingSection = null;
        return section;
    }

    private bool _launchNoticeTaken;

    /// <summary>
    /// Once per launch: "updated" right after an update was installed, "failed" when the updater reported a
    /// failure, otherwise null. Also records the running version for the next launch.
    /// </summary>
    public string? TakeLaunchUpdateNotice()
    {
        if (_launchNoticeTaken) return null;
        _launchNoticeTaken = true;

        var args = Environment.GetCommandLineArgs();
        bool failed = Array.Exists(args, a => string.Equals(a, "--update-failed", StringComparison.OrdinalIgnoreCase));

        var settings = _storageService.CurrentSettings;
        string current = _updateService.CurrentVersion.ToString();
        bool updated = Version.TryParse(settings.LastRunVersion, out var lastRun) && _updateService.CurrentVersion > lastRun;
        if (settings.LastRunVersion != current)
        {
            settings.LastRunVersion = current;
            _ = _storageService.SaveSettingsAsync(settings);
        }

        if (failed) return "failed";
        return updated ? "updated" : null;
    }

    public string CurrentVersionTag => $"v{_updateService.CurrentVersion}";

    private string PromptKey(bool ready) => ready ? UpdateAvailableVersion + ":ready" : UpdateAvailableVersion;

    /// <summary>Whether the prompt for the current update state was already shown and dismissed.</summary>
    public bool IsUpdatePromptDismissed(bool ready) => _storageService.CurrentSettings.DismissedUpdateVersion == PromptKey(ready);

    /// <summary>Remembers that the user saw the prompt for this version (and stage) so it doesn't pop up again.</summary>
    public void DismissUpdatePrompt(bool ready)
    {
        var settings = _storageService.CurrentSettings;
        string key = PromptKey(ready);
        if (settings.DismissedUpdateVersion == key) return;
        settings.DismissedUpdateVersion = key;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    /// <summary>
    /// Called while the app closes: a downloaded update is swapped in silently, so the next launch is
    /// already on the new version.
    /// </summary>
    public void ApplyPendingUpdateOnExit()
    {
        if (_isApplyingUpdate || _stagedUpdate == null || !CanSelfUpdate) return;
        try
        {
            _isApplyingUpdate = true;
            _updateService.ApplyStagedUpdate(_stagedUpdate, restart: false);
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("Update.ApplyOnExit", ex);
        }
    }

    /// <summary>
    /// Startup: tidies old downloads, restores an update staged in an earlier session and starts the
    /// periodic background check (the app often stays open for days, so checking once at launch isn't enough).
    /// </summary>
    public void StartUpdateMonitoring(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _updateService.CleanupOldUpdates();

        var staged = _updateService.GetStagedUpdate();
        if (staged != null && CanSelfUpdate)
        {
            _stagedUpdate = staged;
            UpdateAvailableVersion = staged.Tag;
            IsUpdateAvailable = true;
            IsUpdateReadyToInstall = true;
            RefreshUpdateStatusText();
        }

        _updateTimer = dispatcher.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromSeconds(10);
        _updateTimer.Tick += async (t, _) =>
        {
            // First tick waits for the library's first scan, later ones run hourly
            t.Interval = TimeSpan.FromHours(1);
            try
            {
                await AutoCheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                Helpers.Log.Error("AutoUpdateCheck", ex);
            }
        };
        _updateTimer.Start();
    }

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        settings.AutoCheckUpdates = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    partial void OnIsUpdateReadyToInstallChanged(bool value) => RefreshUpdateStatusText();
    partial void OnIsUpdateAvailableChanged(bool value) => RefreshUpdateStatusText();

    partial void OnCloseButtonReturnsToLibraryChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.CloseButtonReturnsToLibrary == value) return;
        settings.CloseButtonReturnsToLibrary = value;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    partial void OnResumePlaybackChanged(bool value)
    {
        var settings = _storageService.CurrentSettings;
        if (settings.ResumePlayback != value)
        {
            settings.ResumePlayback = value;
            if (!value)
            {
                settings.ResumePositions.Clear();
            }
            _ = _storageService.SaveSettingsAsync(settings);
        }
    }

    private void UpdateLastCheckedText()
    {
        var last = _storageService.CurrentSettings.LastUpdateCheckUtc;
        if (last == null)
        {
            LastCheckedFormatted = _localizationService["Updates_NeverChecked"];
            return;
        }

        var local = last.Value.ToLocalTime();
        string when = local.Date == DateTime.Today
            ? $"{_localizationService["Date_Today"]} {local.ToString("t", _localizationService.CurrentCulture)}"
            : local.ToString("g", _localizationService.CurrentCulture);
        LastCheckedFormatted = string.Format(_localizationService["Updates_LastChecked"], when);
    }

    /// <summary>
    /// Background check while "Check for updates automatically" is on, at most every 6 hours (the GitHub
    /// API allows 60 anonymous requests an hour). A found update shows in the sidebar and as a prompt.
    /// </summary>
    public async Task AutoCheckForUpdatesAsync()
    {
        var settings = _storageService.CurrentSettings;
        if (!settings.AutoCheckUpdates) return;
        // Hourly ticks check at most every 6 hours; right after launch a shorter gap is enough, so an
        // update found in an earlier session shows up again without waiting
        var minGap = _checkedThisSession ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(30);
        if (settings.LastUpdateCheckUtc is DateTime last && DateTime.UtcNow - last < minGap) return;
        _checkedThisSession = true;

        await CheckForUpdatesCoreAsync(silent: true);
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

    private async void CalculateCacheSize()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cacheDir = Path.Combine(localAppData, "Reeled", "Thumbnails");

            // Thousands of thumbnails: enumerate off the UI thread so opening Settings doesn't hitch
            long total = await Task.Run(() =>
            {
                if (!Directory.Exists(cacheDir)) return 0L;
                long sum = 0;
                foreach (var file in new DirectoryInfo(cacheDir).EnumerateFiles())
                {
                    try { sum += file.Length; } catch { }
                }
                return sum;
            });

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
        settings.AccentColor = Helpers.Accent.SystemMode;
        settings.UiZoom = 1.0;
        settings.CloseButtonReturnsToLibrary = false;
        settings.SidebarPosition = SidebarPosition.Left;
        settings.FavoritesNavVisibility = NavSectionVisibility.Always;
        settings.MomentsNavVisibility = NavSectionVisibility.Always;
        settings.LibraryViewMode = LibraryViewMode.Grid;
        settings.ShowMomentsBadges = true;
        settings.LookUpGameIcons = true;
        settings.AlwaysShowTabs = false;
        settings.ReopenTabs = true;
        settings.EnableBackdropBlur = true;
        settings.ViewDensity = ViewDensityMode.Comfortable;
        settings.DateGrouping = DateGroupingMode.None;
        settings.MetadataHoverOnly = false;
        settings.SortIndex = 0;
        settings.ReduceMotion = false;
        settings.ResumePlayback = true;
        settings.BackdropBlurLevel = 1;

        settings.EnableSkeletonLoading = true;
        settings.EnableClipCache = true;
        settings.AutoCheckUpdates = true;

        settings.Language = "System";

        await _storageService.SaveSettingsAsync(settings);

        App.ApplyTheme("Default");
        Helpers.Accent.Apply(Helpers.Accent.SystemMode);
        Helpers.UiZoom.Set(1.0);
        _localizationService.SetLanguage("System");
        Initialize();
        UpdateFormattedStrings();
    }
}
