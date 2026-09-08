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
    private bool _isFullscreen;

    [ObservableProperty]
    private string _statusToast = string.Empty;

    public ObservableCollection<GameClip> Playlist { get; } = new();
    public ObservableCollection<ClipBookmark> Bookmarks { get; } = new();

    private bool _isDraggingSlider;

    public string FormattedCurrentTime =>
        CurrentTime.Hours > 0 ? CurrentTime.ToString(@"hh\:mm\:ss") : CurrentTime.ToString(@"mm\:ss");

    public string FormattedTotalTime =>
        TotalTime.Hours > 0 ? TotalTime.ToString(@"hh\:mm\:ss") : TotalTime.ToString(@"mm\:ss");

    public string FormattedPlaybackRate => $"{PlaybackRate:0.##}x";

    public string FullscreenGlyph => IsFullscreen ? "\uE73F" : "\uE740";

    partial void OnPlaybackRateChanged(float value) => OnPropertyChanged(nameof(FormattedPlaybackRate));
    partial void OnIsFullscreenChanged(bool value) => OnPropertyChanged(nameof(FullscreenGlyph));

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

        Volume = _storageService.CurrentSettings.Volume;
        IsMuted = _storageService.CurrentSettings.IsMuted;
        PlaybackRate = (float)_storageService.CurrentSettings.PlaybackSpeed;
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
    }

    private void SetClip(GameClip clip)
    {
        CurrentClip = clip;
        CurrentTime = TimeSpan.Zero;
        ProgressValue = 0.0;

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
        if (HasNextClip)
        {
            PlayNext();
        }
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        _playbackService.TogglePlayPause();
        IsPlaying = _playbackService.IsPlaying;
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
        ShowToast("+5s");
    }

    [RelayCommand]
    public void SkipBackward()
    {
        _playbackService.SkipSeconds(-5);
        ShowToast("-5s");
    }

    [RelayCommand]
    public void FineForward()
    {
        _playbackService.SkipSeconds(1);
        ShowToast("+1s");
    }

    [RelayCommand]
    public void FineBackward()
    {
        _playbackService.SkipSeconds(-1);
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
        _playbackService.SetVolume(Volume);

        var settings = _storageService.CurrentSettings;
        settings.Volume = Volume;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        _playbackService.ToggleMute();
        ShowToast(IsMuted ? "Muted" : $"Volume {Volume}%");

        var settings = _storageService.CurrentSettings;
        settings.IsMuted = IsMuted;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public void ChangeSpeed(double speed)
    {
        PlaybackRate = (float)speed;
        _playbackService.SetPlaybackRate(PlaybackRate);
        ShowToast($"{speed:0.##}x Speed");

        var settings = _storageService.CurrentSettings;
        settings.PlaybackSpeed = speed;
        _ = _storageService.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    [RelayCommand]
    public void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    [RelayCommand]
    public void BackToHome()
    {
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

    private void ShowToast(string message)
    {
        StatusToast = message;
        _ = Task.Delay(1500).ContinueWith(_ =>
        {
            if (StatusToast == message)
            {
                StatusToast = string.Empty;
            }
        });
    }
}
