using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reeled.Models;
using Reeled.Services;

namespace Reeled.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly ILibVlcPlaybackService _playbackService;
    private readonly ILocalStorageService _storageService;
    private readonly INavigationService _navigationService;

    public ILibVlcPlaybackService PlaybackService => _playbackService;

    [ObservableProperty]
    private GameClip? _currentClip;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private TimeSpan _currentTime = TimeSpan.Zero;

    [ObservableProperty]
    private TimeSpan _totalTime = TimeSpan.Zero;

    [ObservableProperty]
    private double _progressValue; // 0.0 to 100.0

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private float _playbackRate = 1.0f;

    [ObservableProperty]
    private bool _isSidebarOpen;

    [ObservableProperty]
    private bool _isControlsVisible = true;

    [ObservableProperty]
    private RepeatMode _repeatMode = RepeatMode.Off;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private string _statusToast = string.Empty;

    [ObservableProperty]
    private AudioTrackInfo? _selectedAudioTrack;

    [ObservableProperty]
    private bool _hasAudioTracks;

    public string SelectedAudioTrackTitle =>
        SelectedAudioTrack != null ? SelectedAudioTrack.DisplayName : "Audio";

    public ObservableCollection<GameClip> Playlist { get; } = new();
    public ObservableCollection<ClipBookmark> Bookmarks { get; } = new();
    public ObservableCollection<AudioTrackInfo> AudioTracks { get; } = new();

    private bool _isDraggingSlider;

    public string FormattedCurrentTime =>
        CurrentTime.Hours > 0 ? CurrentTime.ToString(@"hh\:mm\:ss") : CurrentTime.ToString(@"mm\:ss");

    public string FormattedTotalTime =>
        TotalTime.Hours > 0 ? TotalTime.ToString(@"hh\:mm\:ss") : TotalTime.ToString(@"mm\:ss");

    public string FormattedPlaybackRate => $"{PlaybackRate:0.##}x";

    public string FullscreenGlyph => IsFullscreen ? "\uE73F" : "\uE740";

    public string VolumeGlyph
    {
        get
        {
            if (IsMuted || Volume == 0) return "\uE74F";
            if (Volume < 33) return "\uE992";
            if (Volume < 66) return "\uE993";
            return "\uE994";
        }
    }

    public string RepeatGlyph => RepeatMode switch
    {
        RepeatMode.One => "\uE8ED",
        _ => "\uE8EE"
    };

    public string RepeatTooltip => RepeatMode switch
    {
        RepeatMode.All => "Repeat: All (Click to repeat current clip)",
        RepeatMode.One => "Repeat: Current Clip (Click to turn off)",
        _ => "Repeat: Off (Click to repeat queue)"
    };

    public bool IsRepeatActive => RepeatMode != RepeatMode.Off;

    public double RepeatOpacity => RepeatMode == RepeatMode.Off ? 0.45 : 1.0;

    partial void OnRepeatModeChanged(RepeatMode value)
    {
        OnPropertyChanged(nameof(RepeatGlyph));
        OnPropertyChanged(nameof(RepeatTooltip));
        OnPropertyChanged(nameof(IsRepeatActive));
        OnPropertyChanged(nameof(RepeatOpacity));
    }

    partial void OnPlaybackRateChanged(float value) => OnPropertyChanged(nameof(FormattedPlaybackRate));
    partial void OnIsFullscreenChanged(bool value) => OnPropertyChanged(nameof(FullscreenGlyph));
    partial void OnVolumeChanged(int value) => OnPropertyChanged(nameof(VolumeGlyph));
    partial void OnIsMutedChanged(bool value) => OnPropertyChanged(nameof(VolumeGlyph));

    public bool HasPreviousClip =>
        CurrentClip != null && Playlist.IndexOf(CurrentClip) > 0;

    public bool HasNextClip =>
        CurrentClip != null && Playlist.IndexOf(CurrentClip) < Playlist.Count - 1;

    public PlayerViewModel(
        ILibVlcPlaybackService playbackService,
        ILocalStorageService storageService,
        INavigationService navigationService)
    {
        _playbackService = playbackService;
        _storageService = storageService;
        _navigationService = navigationService;

        _playbackService.PositionChanged += OnPlaybackPositionChanged;
        _playbackService.TimeChanged += OnPlaybackTimeChanged;
        _playbackService.LengthChanged += OnPlaybackLengthChanged;
        _playbackService.PlaybackStarted += () => IsPlaying = true;
        _playbackService.PlaybackPaused += () => IsPlaying = false;
        _playbackService.PlaybackStopped += () => IsPlaying = false;
        _playbackService.MediaEnded += OnMediaEnded;
        _playbackService.AudioTracksChanged += RefreshAudioTracks;

        Volume = _storageService.CurrentSettings.Volume;
        IsMuted = _storageService.CurrentSettings.IsMuted;
        double initialSpeed = _storageService.CurrentSettings.RememberPlaybackSpeed
            ? _storageService.CurrentSettings.PlaybackSpeed
            : _storageService.CurrentSettings.DefaultPlaybackSpeed;
        if (initialSpeed <= 0.1) initialSpeed = 1.0;
        PlaybackRate = (float)initialSpeed;
        RepeatMode = _storageService.CurrentSettings.RepeatMode;
    }

    public void LoadClip(GameClip clip, IEnumerable<GameClip> playlist)
    {
        Playlist.Clear();
        foreach (var item in playlist)
        {
            Playlist.Add(item);
        }

        SetClip(clip);
    }

    public void AttachVideoView(LibVLCSharp.Platforms.Windows.VideoView videoView, string[] swapChainOptions)
    {
        _playbackService.AttachToVideoView(videoView, swapChainOptions);
        _playbackService.SetVolume(Volume);
        _playbackService.SetMute(IsMuted);
        _playbackService.SetPlaybackRate(PlaybackRate);
    }

    private void SetClip(GameClip clip)
    {
        CurrentClip = clip;
        CurrentTime = TimeSpan.Zero;
        ProgressValue = 0.0;

        if (!_storageService.CurrentSettings.RememberPlaybackSpeed)
        {
            double defaultSpeed = _storageService.CurrentSettings.DefaultPlaybackSpeed;
            if (defaultSpeed <= 0.1) defaultSpeed = 1.0;
            PlaybackRate = (float)defaultSpeed;
            _playbackService.SetPlaybackRate(PlaybackRate);
            OnPropertyChanged(nameof(FormattedPlaybackRate));
        }

        Bookmarks.Clear();
        foreach (var bm in clip.Bookmarks)
        {
            Bookmarks.Add(bm);
        }

        OnPropertyChanged(nameof(FormattedCurrentTime));
        OnPropertyChanged(nameof(FormattedTotalTime));
        OnPropertyChanged(nameof(HasPreviousClip));
        OnPropertyChanged(nameof(HasNextClip));

        _ = _playbackService.PlayMediaAsync(clip.FilePath);
    }

    private void OnPlaybackPositionChanged(float position)
    {
        if (!_isDraggingSlider)
        {
            ProgressValue = position * 100.0;
        }
    }

    private void OnPlaybackTimeChanged(long timeMs)
    {
        if (!_isDraggingSlider)
        {
            CurrentTime = TimeSpan.FromMilliseconds(timeMs);
            OnPropertyChanged(nameof(FormattedCurrentTime));
        }
    }

    private void OnPlaybackLengthChanged(long lengthMs)
    {
        TotalTime = TimeSpan.FromMilliseconds(lengthMs);
        if (CurrentClip != null && CurrentClip.Duration == TimeSpan.Zero)
        {
            CurrentClip.Duration = TotalTime;
        }
        OnPropertyChanged(nameof(FormattedTotalTime));
    }

    private void OnMediaEnded()
    {
        IsPlaying = false;
        if (RepeatMode == RepeatMode.One && CurrentClip != null)
        {
            SetClip(CurrentClip);
        }
        else if (HasNextClip)
        {
            PlayNext();
        }
        else if (RepeatMode == RepeatMode.All && Playlist.Count > 0)
        {
            SetClip(Playlist[0]);
        }
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        _playbackService.TogglePlayPause();
        IsPlaying = _playbackService.IsPlaying;
        ShowToast(IsPlaying ? "▶" : "❚❚");
    }

    [RelayCommand]
    public void Stop()
    {
        _playbackService.Stop();
        ProgressValue = 0;
        CurrentTime = TimeSpan.Zero;
        IsPlaying = false;
        OnPropertyChanged(nameof(FormattedCurrentTime));
        ShowToast("■");
    }

    [RelayCommand]
    public void PlayPrevious()
    {
        if (!HasPreviousClip || CurrentClip == null) return;
        int idx = Playlist.IndexOf(CurrentClip);
        if (idx > 0)
        {
            SetClip(Playlist[idx - 1]);
        }
    }

    [RelayCommand]
    public void PlayNext()
    {
        if (!HasNextClip || CurrentClip == null) return;
        int idx = Playlist.IndexOf(CurrentClip);
        if (idx >= 0 && idx < Playlist.Count - 1)
        {
            SetClip(Playlist[idx + 1]);
        }
    }

    [RelayCommand]
    public void SkipForward()
    {
        _playbackService.SkipSeconds(5);
        if (TotalTime > TimeSpan.Zero)
        {
            CurrentTime = TimeSpan.FromSeconds(Math.Clamp(CurrentTime.TotalSeconds + 5, 0, TotalTime.TotalSeconds));
            ProgressValue = (CurrentTime.TotalSeconds / TotalTime.TotalSeconds) * 100.0;
            OnPropertyChanged(nameof(FormattedCurrentTime));
        }
        ShowToast("+5s");
    }

    [RelayCommand]
    public void SkipBackward()
    {
        _playbackService.SkipSeconds(-5);
        if (TotalTime > TimeSpan.Zero)
        {
            CurrentTime = TimeSpan.FromSeconds(Math.Clamp(CurrentTime.TotalSeconds - 5, 0, TotalTime.TotalSeconds));
            ProgressValue = (CurrentTime.TotalSeconds / TotalTime.TotalSeconds) * 100.0;
            OnPropertyChanged(nameof(FormattedCurrentTime));
        }
        ShowToast("-5s");
    }

    [RelayCommand]
    public void FineForward()
    {
        _playbackService.SkipSeconds(1);
        if (TotalTime > TimeSpan.Zero)
        {
            CurrentTime = TimeSpan.FromSeconds(Math.Clamp(CurrentTime.TotalSeconds + 1, 0, TotalTime.TotalSeconds));
            ProgressValue = (CurrentTime.TotalSeconds / TotalTime.TotalSeconds) * 100.0;
            OnPropertyChanged(nameof(FormattedCurrentTime));
        }
        ShowToast("+1s");
    }

    [RelayCommand]
    public void FineBackward()
    {
        _playbackService.SkipSeconds(-1);
        if (TotalTime > TimeSpan.Zero)
        {
            CurrentTime = TimeSpan.FromSeconds(Math.Clamp(CurrentTime.TotalSeconds - 1, 0, TotalTime.TotalSeconds));
            ProgressValue = (CurrentTime.TotalSeconds / TotalTime.TotalSeconds) * 100.0;
            OnPropertyChanged(nameof(FormattedCurrentTime));
        }
        ShowToast("-1s");
    }

    public void OnSliderDragStarted()
    {
        _isDraggingSlider = true;
    }

    public void OnSliderDeltaChanged(double value)
    {
        _isDraggingSlider = true;
        ProgressValue = value;
        float ratio = (float)(value / 100.0);
        _playbackService.SetPosition(ratio);

        if (TotalTime > TimeSpan.Zero)
        {
            CurrentTime = TimeSpan.FromMilliseconds(TotalTime.TotalMilliseconds * ratio);
            OnPropertyChanged(nameof(FormattedCurrentTime));
        }
    }

    public void OnSliderDragCompleted(double value)
    {
        _isDraggingSlider = false;
        float ratio = (float)(value / 100.0);
        _playbackService.SetPosition(ratio);
    }

    [RelayCommand]
    public async Task AddBookmarkAsync(string? label = null)
    {
        if (CurrentClip == null) return;

        double pos = ProgressValue / 100.0;
        var bookmark = new ClipBookmark
        {
            Timestamp = CurrentTime,
            PositionPercentage = pos,
            Label = string.IsNullOrWhiteSpace(label) ? $"Mark at {FormattedCurrentTime}" : label.Trim()
        };

        Bookmarks.Add(bookmark);
        CurrentClip.Bookmarks.Add(bookmark);

        var settings = _storageService.CurrentSettings;
        if (!settings.Bookmarks.TryGetValue(CurrentClip.FilePath, out var list))
        {
            list = new List<ClipBookmark>();
            settings.Bookmarks[CurrentClip.FilePath] = list;
        }
        list.Add(bookmark);
        await _storageService.SaveSettingsAsync(settings);

        ShowToast($"Saved marker at {bookmark.FormattedTimestamp}");
    }

    [RelayCommand]
    public async Task RemoveBookmarkAsync(ClipBookmark bookmark)
    {
        if (CurrentClip == null || bookmark == null) return;

        Bookmarks.Remove(bookmark);
        CurrentClip.Bookmarks.Remove(bookmark);

        var settings = _storageService.CurrentSettings;
        if (settings.Bookmarks.TryGetValue(CurrentClip.FilePath, out var list))
        {
            list.RemoveAll(b => b.Id == bookmark.Id);
            await _storageService.SaveSettingsAsync(settings);
        }
        ShowToast("Removed moment");
    }

    public async Task UpdateBookmarkLabelAsync(ClipBookmark bookmark, string newLabel)
    {
        if (CurrentClip == null || bookmark == null) return;

        string trimmed = string.IsNullOrWhiteSpace(newLabel) ? $"Mark at {bookmark.FormattedTimestamp}" : newLabel.Trim();
        bookmark.Label = trimmed;

        var settings = _storageService.CurrentSettings;
        if (settings.Bookmarks.TryGetValue(CurrentClip.FilePath, out var list))
        {
            var match = list.Find(b => b.Id == bookmark.Id);
            if (match != null)
            {
                match.Label = trimmed;
            }
            await _storageService.SaveSettingsAsync(settings);
        }

        ShowToast($"Renamed to \"{trimmed}\"");
    }

    [RelayCommand]
    public void JumpToBookmark(ClipBookmark bookmark)
    {
        if (bookmark == null) return;
        _playbackService.SeekTime((long)bookmark.Timestamp.TotalMilliseconds);
        ShowToast($"Jumped to {bookmark.FormattedTimestamp}");
    }

    [RelayCommand]
    public void SetVolume(int newVolume)
    {
        Volume = Math.Clamp(newVolume, 0, 100);
        if (Volume > 0 && IsMuted)
        {
            IsMuted = false;
            _playbackService.SetMute(false);
        }
        _playbackService.SetVolume(Volume);
        OnPropertyChanged(nameof(VolumeGlyph));
        ShowToast(IsMuted ? "Muted" : $"Volume {Volume}%");

        var settings = _storageService.CurrentSettings;
        settings.Volume = Volume;
        settings.IsMuted = IsMuted;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        if (!IsMuted && Volume == 0)
        {
            Volume = 50;
            _playbackService.SetVolume(50);
        }
        _playbackService.SetMute(IsMuted);
        OnPropertyChanged(nameof(VolumeGlyph));
        ShowToast(IsMuted ? "Muted" : $"Volume {Volume}%");

        var settings = _storageService.CurrentSettings;
        settings.IsMuted = IsMuted;
        settings.Volume = Volume;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public void ToggleRepeatMode()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };

        var toast = RepeatMode switch
        {
            RepeatMode.All => "Repeat: All",
            RepeatMode.One => "Repeat: Current Clip",
            _ => "Repeat: Off"
        };
        ShowToast(toast);

        var settings = _storageService.CurrentSettings;
        settings.RepeatMode = RepeatMode;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public void ChangeSpeed(double speed)
    {
        PlaybackRate = (float)speed;
        _playbackService.SetPlaybackRate(PlaybackRate);
        OnPropertyChanged(nameof(FormattedPlaybackRate));
        ShowToast($"{speed:0.##}x Speed");

        var settings = _storageService.CurrentSettings;
        settings.PlaybackSpeed = speed;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public void AdjustSpeed(double delta)
    {
        double newSpeed = Math.Round(PlaybackRate + delta, 2);
        newSpeed = Math.Clamp(newSpeed, 0.25, 3.0);
        ChangeSpeed(newSpeed);
    }

    [RelayCommand]
    public void ResetSpeed()
    {
        ChangeSpeed(1.0);
    }

    public void RefreshAudioTracks()
    {
        var tracks = _playbackService.GetAudioTracks();
        AudioTracks.Clear();
        foreach (var t in tracks)
        {
            AudioTracks.Add(t);
        }
        SelectedAudioTrack = AudioTracks.FirstOrDefault(t => t.IsSelected);
        HasAudioTracks = AudioTracks.Count > 0;
        OnPropertyChanged(nameof(SelectedAudioTrackTitle));
    }

    public void SelectAudioTrack(int trackId)
    {
        bool success = _playbackService.SetAudioTrack(trackId);
        if (success)
        {
            RefreshAudioTracks();
            string name = SelectedAudioTrack?.DisplayName ?? (trackId == -1 ? "Audio Disabled" : "Audio track changed");
            ShowToast(name);
        }
    }

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    [RelayCommand]
    public void ToggleFullscreen()
    {
        if (App.Window is MainWindow mainWindow)
        {
            mainWindow.ToggleFullscreen();
        }
        else
        {
            IsFullscreen = !IsFullscreen;
        }
    }

    [RelayCommand]
    public void BackToHome()
    {
        if (IsFullscreen)
        {
            try
            {
                App.Window.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            }
            catch { }
            IsFullscreen = false;
        }
        _playbackService.Stop();
        _navigationService.NavigateToHome();
    }

    public void SelectPlaylistClip(GameClip clip)
    {
        if (clip != null && clip != CurrentClip)
        {
            SetClip(clip);
        }
    }

    public void ShowToast(string message)
    {
        if (StatusToast == message)
        {
            StatusToast = string.Empty;
        }
        StatusToast = message;
    }
}
