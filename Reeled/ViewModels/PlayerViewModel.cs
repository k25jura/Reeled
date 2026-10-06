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
    private readonly ILocalizationService _localizationService;

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
    private ClipBookmark? _activeBookmark;

    [ObservableProperty]
    private AudioTrackInfo? _selectedAudioTrack;

    [ObservableProperty]
    private bool _hasAudioTracks;

    [ObservableProperty]
    private SubtitleTrackInfo? _selectedSubtitleTrack;

    [ObservableProperty]
    private bool _hasSubtitles;

    public IReadOnlyList<int> ActiveAudioTracks => _playbackService.ActiveAudioTracks;

    public long LastPositionTimeTicks { get; private set; }
    public double AnchorSeconds { get; private set; }
    private long _seekSuppressionUntilTicks;

    public string SelectedAudioTrackTitle
    {
        get
        {
            var active = _playbackService.ActiveAudioTracks;
            if (active.Count == 0)
            {
                return _localizationService["Player_DisableAudio"];
            }
            if (active.Count == 1)
            {
                int id = active.First();
                var track = AudioTracks.FirstOrDefault(t => t.Id == id);
                return track?.DisplayName ?? "Audio";
            }
            return string.Format(_localizationService["Player_MixedAudioTracks"], active.Count);
        }
    }

    public ObservableCollection<GameClip> Playlist { get; } = new();
    public ObservableCollection<ClipBookmark> Bookmarks { get; } = new();
    public ObservableCollection<AudioTrackInfo> AudioTracks { get; } = new();
    public ObservableCollection<SubtitleTrackInfo> SubtitleTracks { get; } = new();

    private bool _isDraggingSlider;

    public string FormattedCurrentTime =>
        CurrentTime.Hours > 0 ? CurrentTime.ToString(@"hh\:mm\:ss") : CurrentTime.ToString(@"mm\:ss");

    public string FormattedTotalTime =>
        TotalTime.Hours > 0 ? TotalTime.ToString(@"hh\:mm\:ss") : TotalTime.ToString(@"mm\:ss");

    public string FormattedPlaybackRate => $"{PlaybackRate:0.##}x";

    public string FullscreenGlyph => IsFullscreen ? "\uE73F" : "\uE740";

    public int MaxVolume => _storageService.CurrentSettings.MaxVolume > 0 ? _storageService.CurrentSettings.MaxVolume : 200;

    public bool IsVolumeBoosted => Volume > 100;

    public int SliderVolume => Math.Clamp(Volume, 0, 100);

    public Microsoft.UI.Xaml.Media.Brush VolumeSliderBrush => IsVolumeBoosted
        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 160, 0))
        : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

    public Microsoft.UI.Xaml.Media.Brush VolumeTextBrush => IsVolumeBoosted
        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 160, 0))
        : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 192, 192, 192));

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
        RepeatMode.All => _localizationService["Player_Repeat_All_Tip"],
        RepeatMode.One => _localizationService["Player_Repeat_One_Tip"],
        _ => _localizationService["Player_Repeat_Off_Tip"]
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

    partial void OnPlaybackRateChanged(float value)
    {
        _playbackService?.SetPlaybackRate(value);
        OnPropertyChanged(nameof(FormattedPlaybackRate));
    }
    partial void OnIsFullscreenChanged(bool value) => OnPropertyChanged(nameof(FullscreenGlyph));
    partial void OnVolumeChanged(int value)
    {
        OnPropertyChanged(nameof(VolumeGlyph));
        OnPropertyChanged(nameof(IsVolumeBoosted));
        OnPropertyChanged(nameof(SliderVolume));
        OnPropertyChanged(nameof(VolumeSliderBrush));
        OnPropertyChanged(nameof(VolumeTextBrush));
    }
    partial void OnIsMutedChanged(bool value) => OnPropertyChanged(nameof(VolumeGlyph));

    public bool HasPreviousClip =>
        CurrentClip != null && Playlist.Count > 0;

    public bool HasNextClip =>
        CurrentClip != null && Playlist.Count > 1;

    public PlayerViewModel(
        ILibVlcPlaybackService playbackService,
        ILocalStorageService storageService,
        INavigationService navigationService,
        ILocalizationService localizationService)
    {
        _playbackService = playbackService;
        _storageService = storageService;
        _navigationService = navigationService;
        _localizationService = localizationService;

        _localizationService.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(RepeatTooltip));
        };

        _playbackService.PositionChanged += OnPlaybackPositionChanged;
        _playbackService.TimeChanged += OnPlaybackTimeChanged;
        _playbackService.LengthChanged += OnPlaybackLengthChanged;
        _playbackService.PlaybackStarted += () => IsPlaying = true;
        _playbackService.PlaybackPaused += () => IsPlaying = false;
        _playbackService.PlaybackStopped += () => IsPlaying = false;
        _playbackService.MediaEnded += OnMediaEnded;
        _playbackService.AudioTracksChanged += RefreshAudioTracks;
        _playbackService.SubtitlesChanged += RefreshSubtitleTracks;

        Volume = Math.Clamp(_storageService.CurrentSettings.DefaultVolume, 0, MaxVolume);
        IsMuted = _storageService.CurrentSettings.StartMuted;
        double initialSpeed = _storageService.CurrentSettings.RememberPlaybackSpeed
            ? _storageService.CurrentSettings.PlaybackSpeed
            : _storageService.CurrentSettings.DefaultPlaybackSpeed;
        if (initialSpeed <= 0.1) initialSpeed = 1.0;
        PlaybackRate = (float)initialSpeed;
        _playbackService.SetPlaybackRate(PlaybackRate);
        RepeatMode = _storageService.CurrentSettings.DefaultRepeatMode;
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
        foreach (var item in Playlist)
        {
            item.IsActive = string.Equals(item.FilePath, clip.FilePath, StringComparison.OrdinalIgnoreCase);
        }
        CurrentTime = TimeSpan.Zero;
        ProgressValue = 0.0;

        double speedToApply = _storageService.CurrentSettings.RememberPlaybackSpeed
            ? _storageService.CurrentSettings.PlaybackSpeed
            : _storageService.CurrentSettings.DefaultPlaybackSpeed;
        if (speedToApply <= 0.1) speedToApply = 1.0;
        PlaybackRate = (float)speedToApply;
        _playbackService.SetPlaybackRate(PlaybackRate);
        OnPropertyChanged(nameof(FormattedPlaybackRate));

        IsMuted = _storageService.CurrentSettings.StartMuted;
        _playbackService.SetMute(IsMuted);

        Volume = Math.Clamp(_storageService.CurrentSettings.DefaultVolume, 0, MaxVolume);
        _playbackService.SetVolume(Volume);
        OnPropertyChanged(nameof(VolumeGlyph));

        RepeatMode = _storageService.CurrentSettings.DefaultRepeatMode;

        Bookmarks.Clear();
        foreach (var bm in clip.Bookmarks.OrderBy(b => b.Timestamp))
        {
            Bookmarks.Add(bm);
        }
        ActiveBookmark = null;

        OnPropertyChanged(nameof(FormattedCurrentTime));
        OnPropertyChanged(nameof(FormattedTotalTime));
        OnPropertyChanged(nameof(HasPreviousClip));
        OnPropertyChanged(nameof(HasNextClip));

        AudioTracks.Clear();
        SubtitleTracks.Clear();
        RefreshAudioTracks();
        RefreshSubtitleTracks();

        _ = _playbackService.PlayMediaAsync(clip.FilePath);
    }

    private void OnPlaybackPositionChanged(float position)
    {
        if (!_isDraggingSlider && !IsPlaying)
        {
            ProgressValue = position * 100.0;
        }
    }

    private void OnPlaybackTimeChanged(long timeMs)
    {
        double sec = timeMs / 1000.0;
        long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        // If we recently initiated a seek or skip, ignore incoming stale time ticks from before the seek
        if (nowTicks < _seekSuppressionUntilTicks)
        {
            if (Math.Abs(sec - CurrentTime.TotalSeconds) > 1.2)
            {
                return;
            }
            _seekSuppressionUntilTicks = 0;
        }

        LastPositionTimeTicks = nowTicks;
        AnchorSeconds = sec;
        if (!_isDraggingSlider)
        {
            UpdatePlaybackTime(TimeSpan.FromMilliseconds(timeMs));
            // Only update ProgressValue if not playing (to avoid 250ms snap-backs against 50fps timer)
            // or if the drift between ProgressValue and sec is significant (> 0.5s)
            if (!IsPlaying || TotalTime <= TimeSpan.Zero || Math.Abs(CurrentTime.TotalSeconds - sec) > 0.5)
            {
                if (TotalTime > TimeSpan.Zero)
                {
                    ProgressValue = (sec / TotalTime.TotalSeconds) * 100.0;
                }
            }
        }
    }

    public void UpdatePlaybackTime(TimeSpan currentTime)
    {
        CurrentTime = currentTime;
        OnPropertyChanged(nameof(FormattedCurrentTime));

        ClipBookmark? matching = null;
        foreach (var bm in Bookmarks)
        {
            if (bm.IsRange && bm.EndTimestamp.HasValue)
            {
                if (currentTime >= bm.Timestamp && currentTime <= bm.EndTimestamp.Value)
                {
                    matching = bm;
                    break;
                }
            }
            else
            {
                if (Math.Abs((currentTime - bm.Timestamp).TotalSeconds) <= 1.5)
                {
                    matching = bm;
                    break;
                }
            }
        }

        ActiveBookmark = matching;
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
        if (CurrentClip == null || Playlist.Count == 0) return;

        if (CurrentTime.TotalSeconds > 3.0)
        {
            _playbackService.SeekTime(0);
            CurrentTime = TimeSpan.Zero;
            ProgressValue = 0;
            OnPropertyChanged(nameof(FormattedCurrentTime));
            ShowToast("00:00");
            return;
        }

        int idx = Playlist.IndexOf(CurrentClip);
        if (idx > 0)
        {
            SetClip(Playlist[idx - 1]);
        }
        else if (idx == 0 && Playlist.Count > 1)
        {
            SetClip(Playlist[Playlist.Count - 1]);
        }
        else
        {
            _playbackService.SeekTime(0);
            CurrentTime = TimeSpan.Zero;
            ProgressValue = 0;
            OnPropertyChanged(nameof(FormattedCurrentTime));
        }
    }

    [RelayCommand]
    public void PlayNext()
    {
        if (CurrentClip == null || Playlist.Count == 0) return;

        int idx = Playlist.IndexOf(CurrentClip);
        if (idx >= 0 && idx < Playlist.Count - 1)
        {
            SetClip(Playlist[idx + 1]);
        }
        else if (Playlist.Count > 1)
        {
            SetClip(Playlist[0]);
        }
    }

    [RelayCommand]
    public void SkipForward()
    {
        SkipBySeconds(5);
        ShowToast("+5s");
    }

    [RelayCommand]
    public void SkipBackward()
    {
        SkipBySeconds(-5);
        ShowToast("-5s");
    }

    [RelayCommand]
    public void FineForward()
    {
        SkipBySeconds(1);
        ShowToast("+1s");
    }

    [RelayCommand]
    public void FineBackward()
    {
        SkipBySeconds(-1);
        ShowToast("-1s");
    }

    private void SkipBySeconds(int seconds)
    {
        if (TotalTime <= TimeSpan.Zero) return;
        double targetSec = Math.Clamp(CurrentTime.TotalSeconds + seconds, 0, TotalTime.TotalSeconds);
        long targetMs = (long)(targetSec * 1000.0);

        AnchorSeconds = targetSec;
        LastPositionTimeTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _seekSuppressionUntilTicks = LastPositionTimeTicks + (long)(0.40 * System.Diagnostics.Stopwatch.Frequency);

        CurrentTime = TimeSpan.FromSeconds(targetSec);
        ProgressValue = (targetSec / TotalTime.TotalSeconds) * 100.0;
        OnPropertyChanged(nameof(FormattedCurrentTime));

        _playbackService.SeekTime(targetMs);
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
        if (TotalTime > TimeSpan.Zero)
        {
            double targetSec = TotalTime.TotalSeconds * ratio;
            AnchorSeconds = targetSec;
            LastPositionTimeTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _seekSuppressionUntilTicks = LastPositionTimeTicks + (long)(0.40 * System.Diagnostics.Stopwatch.Frequency);
            CurrentTime = TimeSpan.FromSeconds(targetSec);
            ProgressValue = value;
            OnPropertyChanged(nameof(FormattedCurrentTime));
        }
        _playbackService.SetPosition(ratio);
    }

    public async Task AddBookmarkWithDetailsAsync(string? label, TimeSpan? endTimestamp, string? colorHex)
    {
        if (CurrentClip == null) return;

        double pos = ProgressValue / 100.0;
        double? endPos = endTimestamp.HasValue && TotalTime > TimeSpan.Zero
            ? Math.Clamp(endTimestamp.Value.TotalSeconds / TotalTime.TotalSeconds, 0.0, 1.0)
            : null;

        string finalLabel;
        if (string.IsNullOrWhiteSpace(label))
        {
            if (endTimestamp.HasValue && endTimestamp.Value > CurrentTime)
            {
                string endStr = endTimestamp.Value.Hours > 0
                    ? endTimestamp.Value.ToString(@"hh\:mm\:ss")
                    : endTimestamp.Value.ToString(@"mm\:ss");
                finalLabel = $"Moment {FormattedCurrentTime} - {endStr}";
            }
            else
            {
                finalLabel = $"Mark at {FormattedCurrentTime}";
            }
        }
        else
        {
            finalLabel = label.Trim();
        }

        var bookmark = new ClipBookmark
        {
            Timestamp = CurrentTime,
            PositionPercentage = pos,
            EndTimestamp = endTimestamp,
            EndPositionPercentage = endPos,
            ColorHex = string.IsNullOrWhiteSpace(colorHex) ? "#FFD700" : colorHex,
            Label = finalLabel
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

        SortBookmarks();

        ShowToast(string.Format(_localizationService["Player_Toast_SavedMarker"], bookmark.FormattedTimestamp));
    }

    [RelayCommand]
    public async Task AddBookmarkAsync(string? label = null)
    {
        await AddBookmarkWithDetailsAsync(label, null, null);
    }

    [RelayCommand]
    public async Task RemoveBookmarkAsync(ClipBookmark bookmark)
    {
        if (CurrentClip == null || bookmark == null) return;

        if (ActiveBookmark?.Id == bookmark.Id)
        {
            ActiveBookmark = null;
        }

        Bookmarks.Remove(bookmark);
        CurrentClip.Bookmarks.Remove(bookmark);

        var settings = _storageService.CurrentSettings;
        if (settings.Bookmarks.TryGetValue(CurrentClip.FilePath, out var list))
        {
            list.RemoveAll(b => b.Id == bookmark.Id);
            await _storageService.SaveSettingsAsync(settings);
        }
        ShowToast(_localizationService["Player_Toast_RemovedMoment"]);
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

        ShowToast(string.Format(_localizationService["Player_Toast_RenamedMoment"], trimmed));
    }

    public async Task UpdateBookmarkDetailsAsync(ClipBookmark bookmark, TimeSpan newStart, TimeSpan? newEnd, string newLabel, string newColor)
    {
        if (CurrentClip == null || bookmark == null) return;

        bookmark.Timestamp = newStart;
        bookmark.EndTimestamp = newEnd;
        bookmark.PositionPercentage = TotalTime > TimeSpan.Zero
            ? Math.Clamp(newStart.TotalSeconds / TotalTime.TotalSeconds, 0.0, 1.0)
            : 0.0;
        bookmark.EndPositionPercentage = newEnd.HasValue && TotalTime > TimeSpan.Zero
            ? Math.Clamp(newEnd.Value.TotalSeconds / TotalTime.TotalSeconds, 0.0, 1.0)
            : null;

        string trimmed = string.IsNullOrWhiteSpace(newLabel) ? $"Mark at {bookmark.FormattedTimestamp}" : newLabel.Trim();
        bookmark.Label = trimmed;

        if (!string.IsNullOrWhiteSpace(newColor))
        {
            bookmark.ColorHex = newColor;
        }

        var settings = _storageService.CurrentSettings;
        if (settings.Bookmarks.TryGetValue(CurrentClip.FilePath, out var list))
        {
            var match = list.Find(b => b.Id == bookmark.Id);
            if (match != null)
            {
                match.Timestamp = bookmark.Timestamp;
                match.EndTimestamp = bookmark.EndTimestamp;
                match.PositionPercentage = bookmark.PositionPercentage;
                match.EndPositionPercentage = bookmark.EndPositionPercentage;
                match.Label = bookmark.Label;
                match.ColorHex = bookmark.ColorHex;
            }
            await _storageService.SaveSettingsAsync(settings);
        }

        SortBookmarks();

        ShowToast(string.Format(_localizationService["Player_Toast_ChangedMoment"], bookmark.Label));
    }

    private void SortBookmarks()
    {
        var sorted = Bookmarks.OrderBy(b => b.Timestamp).ToList();
        Bookmarks.Clear();
        foreach (var b in sorted)
        {
            Bookmarks.Add(b);
        }
        if (CurrentClip != null)
        {
            var clipSorted = CurrentClip.Bookmarks.OrderBy(b => b.Timestamp).ToList();
            CurrentClip.Bookmarks.Clear();
            foreach (var b in clipSorted)
            {
                CurrentClip.Bookmarks.Add(b);
            }
        }
    }

    [RelayCommand]
    public void JumpToBookmark(ClipBookmark bookmark)
    {
        if (bookmark == null) return;
        double targetSec = bookmark.Timestamp.TotalSeconds;
        AnchorSeconds = targetSec;
        LastPositionTimeTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _seekSuppressionUntilTicks = LastPositionTimeTicks + (long)(0.40 * System.Diagnostics.Stopwatch.Frequency);
        CurrentTime = bookmark.Timestamp;
        if (TotalTime > TimeSpan.Zero)
        {
            ProgressValue = Math.Clamp(targetSec / TotalTime.TotalSeconds * 100.0, 0.0, 100.0);
        }
        OnPropertyChanged(nameof(FormattedCurrentTime));
        _playbackService.SeekTime((long)bookmark.Timestamp.TotalMilliseconds);
        ShowToast(string.Format(_localizationService["Player_Toast_JumpedTo"], bookmark.FormattedTimestamp));
    }

    [RelayCommand]
    public void SetVolume(int newVolume)
    {
        Volume = Math.Clamp(newVolume, 0, MaxVolume);
        if (Volume > 0 && IsMuted)
        {
            IsMuted = false;
            _playbackService.SetMute(false);
        }
        _playbackService.SetVolume(Volume);
        OnPropertyChanged(nameof(VolumeGlyph));
        OnPropertyChanged(nameof(IsVolumeBoosted));
        OnPropertyChanged(nameof(SliderVolume));
        OnPropertyChanged(nameof(VolumeSliderBrush));
        OnPropertyChanged(nameof(VolumeTextBrush));

        string toast = IsMuted 
            ? _localizationService["Player_Toast_Muted"] 
            : string.Format(_localizationService["Player_Toast_Volume"], Volume);
        ShowToast(toast);

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
        ShowToast(IsMuted ? _localizationService["Player_Toast_Muted"] : string.Format(_localizationService["Player_Toast_Volume"], Volume));

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
            RepeatMode.All => _localizationService["Player_Repeat_All_Toast"],
            RepeatMode.One => _localizationService["Player_Repeat_One_Toast"],
            _ => _localizationService["Player_Repeat_Off_Toast"]
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
        ShowToast(string.Format(_localizationService["Player_Toast_Speed"], $"{speed:0.##}"));

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
        OnPropertyChanged(nameof(ActiveAudioTracks));
        OnPropertyChanged(nameof(SelectedAudioTrackTitle));
    }

    public void SelectAudioTrack(int trackId)
    {
        bool success = _playbackService.SetAudioTrack(trackId);
        if (success)
        {
            RefreshAudioTracks();
            string name = SelectedAudioTrack?.DisplayName ?? (trackId == -1 ? _localizationService["Player_DisableAudio"] : "Audio track changed");
            ShowToast(name);
        }
    }

    public void ToggleAudioTrack(int trackId)
    {
        var current = new HashSet<int>(_playbackService.ActiveAudioTracks);
        if (trackId == -1)
        {
            current.Clear();
        }
        else
        {
            if (current.Contains(trackId))
            {
                current.Remove(trackId);
            }
            else
            {
                current.Add(trackId);
            }
        }

        _playbackService.SetAudioTracks(current);
        RefreshAudioTracks();
        ShowToast(SelectedAudioTrackTitle);
    }

    public void DisableAudio()
    {
        _playbackService.SetAudioTracks(Array.Empty<int>());
        RefreshAudioTracks();
        ShowToast(_localizationService["Player_DisableAudio"]);
    }

    public void RefreshSubtitleTracks()
    {
        var tracks = _playbackService.GetSubtitleTracks();
        SubtitleTracks.Clear();
        foreach (var t in tracks)
        {
            SubtitleTracks.Add(t);
        }
        SelectedSubtitleTrack = SubtitleTracks.FirstOrDefault(t => t.IsSelected);
        HasSubtitles = SubtitleTracks.Count > 0;
    }

    public void SelectSubtitleTrack(int trackId)
    {
        bool success = _playbackService.SetSubtitleTrack(trackId);
        if (success)
        {
            RefreshSubtitleTracks();
            string name = SelectedSubtitleTrack?.DisplayName ?? (trackId == -1 ? _localizationService["Player_Toast_SubtitlesDisabled"] : "Subtitles");
            ShowToast(name);
        }
    }

    public async Task AddSubtitleFileAsync(IntPtr windowHandle)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add(".ass");
            picker.FileTypeFilter.Add(".ssa");
            picker.FileTypeFilter.Add(".srt");
            picker.FileTypeFilter.Add(".vtt");
            picker.FileTypeFilter.Add(".sub");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                bool added = _playbackService.AddSubtitleFile(file.Path);
                if (added)
                {
                    RefreshSubtitleTracks();
                    ShowToast(string.Format(_localizationService["Player_Toast_SubtitlesLoaded"], file.Name));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding subtitle file: {ex.Message}");
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
                if (App.Window is MainWindow mainWindow)
                {
                    mainWindow.SetFullscreen(false);
                }
                else
                {
                    App.Window.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
                    IsFullscreen = false;
                }
            }
            catch { }
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
        if (!_storageService.CurrentSettings.EnableOsdNotifications)
        {
            return;
        }

        if (StatusToast == message)
        {
            StatusToast = string.Empty;
        }
        StatusToast = message;
    }
}
