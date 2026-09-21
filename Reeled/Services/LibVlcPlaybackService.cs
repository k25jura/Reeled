using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;

namespace Reeled.Services;

public class LibVlcPlaybackService : ILibVlcPlaybackService
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private DispatcherQueue? _dispatcherQueue;
    private string? _pendingFilePath;
    private string? _currentFilePath;
    private string[]? _currentSwapChainOptions;
    private bool _isDisposed;

    public event Action<float>? PositionChanged;
    public event Action<long>? TimeChanged;
    public event Action<long>? LengthChanged;
    public event Action? PlaybackStarted;
    public event Action? PlaybackPaused;
    public event Action? PlaybackStopped;
    public event Action? MediaEnded;
    public event Action? AudioTracksChanged;
    public event Action? SubtitlesChanged;

    public MediaPlayer? CurrentMediaPlayer => _mediaPlayer;
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public float Position => _mediaPlayer?.Position ?? 0f;
    public long Time => _mediaPlayer?.Time ?? 0L;
    public long Length => _mediaPlayer?.Length ?? 0L;
    private int _storedVolume = 100;
    private bool _storedMute = false;
    private float _storedPlaybackRate = 1.0f;
    public int Volume => _mediaPlayer != null ? _mediaPlayer.Volume : _storedVolume;
    public bool IsMuted => _mediaPlayer != null ? _mediaPlayer.Mute : _storedMute;
    public float PlaybackRate => _mediaPlayer != null ? _mediaPlayer.Rate : _storedPlaybackRate;
    public int CurrentAudioTrack => _mediaPlayer?.AudioTrack ?? -1;

    private readonly List<MediaPlayer> _secondaryAudioPlayers = new();
    private readonly List<int> _activeAudioTracks = new();

    public IReadOnlyList<int> ActiveAudioTracks
    {
        get
        {
            lock (_secondaryAudioPlayers)
            {
                return _activeAudioTracks.Count > 0 ? _activeAudioTracks.ToArray() : (CurrentAudioTrack > 0 ? new[] { CurrentAudioTrack } : Array.Empty<int>());
            }
        }
    }

    public IReadOnlyList<Reeled.Models.AudioTrackInfo> GetAudioTracks()
    {
        var list = new List<Reeled.Models.AudioTrackInfo>();
        if (_mediaPlayer == null) return list;

        try
        {
            var descriptions = _mediaPlayer.AudioTrackDescription;
            int current = _mediaPlayer.AudioTrack;
            lock (_secondaryAudioPlayers)
            {
                if (descriptions != null)
                {
                    foreach (var desc in descriptions)
                    {
                        if (desc.Id == -1) continue; // Skip LibVLC internal "Disable" track as UI provides localized disable option
                        bool isSel = _activeAudioTracks.Count > 0 ? _activeAudioTracks.Contains(desc.Id) : (desc.Id == current);
                        list.Add(new Reeled.Models.AudioTrackInfo
                        {
                            Id = desc.Id,
                            Name = desc.Name,
                            IsSelected = isSel
                        });
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        return list;
    }

    public bool SetAudioTrack(int trackId)
    {
        return SetAudioTracks(trackId > 0 ? new[] { trackId } : Array.Empty<int>());
    }

    public bool SetAudioTracks(IEnumerable<int> trackIds)
    {
        var validTracks = new List<int>();
        foreach (int t in trackIds)
        {
            if (t > 0 && !validTracks.Contains(t))
            {
                validTracks.Add(t);
            }
        }

        lock (_secondaryAudioPlayers)
        {
            _activeAudioTracks.Clear();
            _activeAudioTracks.AddRange(validTracks);
        }

        if (validTracks.Count == 0)
        {
            StopSecondaryAudio();
            if (_mediaPlayer != null)
            {
                _mediaPlayer.SetAudioTrack(-1);
            }
            AudioTracksChanged?.Invoke();
            return true;
        }

        int primaryTrack = validTracks[0];
        bool success = false;
        if (_mediaPlayer != null)
        {
            success = _mediaPlayer.SetAudioTrack(primaryTrack);
        }

        var secondaryTracks = new List<int>();
        for (int i = 1; i < validTracks.Count; i++)
        {
            secondaryTracks.Add(validTracks[i]);
        }
        SyncSecondaryAudioTracks(secondaryTracks);

        AudioTracksChanged?.Invoke();
        return success;
    }

    private void StopSecondaryAudio()
    {
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                try
                {
                    p.Stop();
                    p.Dispose();
                }
                catch { }
            }
            _secondaryAudioPlayers.Clear();
        }
    }

    private void SyncSecondaryAudioTracks(List<int> secondaryTracks)
    {
        StopSecondaryAudio();
        if (_libVLC == null || string.IsNullOrWhiteSpace(_currentFilePath) || secondaryTracks.Count == 0)
            return;

        string filePath = _currentFilePath;
        lock (_secondaryAudioPlayers)
        {
            foreach (int trackId in secondaryTracks)
            {
                try
                {
                    var slavePlayer = new MediaPlayer(_libVLC);
                    var media = new Media(_libVLC, filePath, FromType.FromPath);
                    media.AddOption(":no-video");
                    media.AddOption(":no-spu");
                    media.AddOption(":no-sub-autodetect-file");
                    media.AddOption($":audio-track-id={trackId}");
                    slavePlayer.Media = media;
                    slavePlayer.Volume = Math.Clamp(Volume, 0, 150);
                    slavePlayer.Mute = IsMuted;
                    slavePlayer.SetRate(PlaybackRate);

                    int targetTrack = trackId;
                    slavePlayer.Playing += (s, e) =>
                    {
                        Dispatch(() =>
                        {
                            try
                            {
                                slavePlayer.SetAudioTrack(targetTrack);
                                if (_mediaPlayer != null)
                                {
                                    long t = _mediaPlayer.Time;
                                    if (t > 0 && Math.Abs(slavePlayer.Time - t) > 150)
                                    {
                                        slavePlayer.Time = t;
                                    }
                                    if (!_mediaPlayer.IsPlaying)
                                    {
                                        slavePlayer.Pause();
                                    }
                                }
                            }
                            catch { }
                        });
                    };

                    slavePlayer.Play();

                    Task.Run(async () =>
                    {
                        for (int r = 0; r < 5; r++)
                        {
                            await Task.Delay(100);
                            try
                            {
                                if (slavePlayer.AudioTrack != targetTrack)
                                {
                                    slavePlayer.SetAudioTrack(targetTrack);
                                }
                                if (_mediaPlayer != null)
                                {
                                    long t = _mediaPlayer.Time;
                                    if (t > 0 && Math.Abs(slavePlayer.Time - t) > 200)
                                    {
                                        slavePlayer.Time = t;
                                    }
                                    if (!_mediaPlayer.IsPlaying && slavePlayer.IsPlaying)
                                    {
                                        slavePlayer.Pause();
                                    }
                                }
                            }
                            catch { }
                        }
                    });

                    _secondaryAudioPlayers.Add(slavePlayer);
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText("reeled_crash.log", $"[SecondaryAudio Error] {ex}\n"); } catch { }
                }
            }
        }
    }

    public int CurrentSubtitleTrack => _mediaPlayer?.Spu ?? -1;

    public IReadOnlyList<Reeled.Models.SubtitleTrackInfo> GetSubtitleTracks()
    {
        var list = new List<Reeled.Models.SubtitleTrackInfo>();
        if (_mediaPlayer == null) return list;

        try
        {
            var descriptions = _mediaPlayer.SpuDescription;
            int current = _mediaPlayer.Spu;
            if (descriptions != null)
            {
                foreach (var desc in descriptions)
                {
                    if (desc.Id == -1) continue; // Skip LibVLC internal "Disable" track as UI provides localized disable option
                    list.Add(new Reeled.Models.SubtitleTrackInfo
                    {
                        Id = desc.Id,
                        Name = desc.Name,
                        IsSelected = desc.Id == current
                    });
                }
            }
        }
        catch (Exception)
        {
        }

        return list;
    }

    public bool SetSubtitleTrack(int trackId)
    {
        if (_mediaPlayer == null) return false;
        try
        {
            bool success = _mediaPlayer.SetSpu(trackId);
            SubtitlesChanged?.Invoke();
            return success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool AddSubtitleFile(string filePath)
    {
        if (_mediaPlayer == null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            string uri = new Uri(filePath).AbsoluteUri;
            bool success = _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, uri, select: true);
            SubtitlesChanged?.Invoke();
            return success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void SetSubtitleDelay(long delayMs)
    {
        if (_mediaPlayer == null) return;
        try
        {
            _mediaPlayer.SetSpuDelay(delayMs * 1000L);
        }
        catch { }
    }

    private static bool _coreInitialized;
    private static readonly object _coreLock = new();

    private static void EnsureCoreInitialized()
    {
        if (_coreInitialized) return;
        lock (_coreLock)
        {
            if (_coreInitialized) return;
            try
            {
                string libvlcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libvlc", "win-x64");
                if (Directory.Exists(libvlcDir))
                {
                    Core.Initialize(libvlcDir);
                }
                else
                {
                    Core.Initialize();
                }
                _coreInitialized = true;
            }
            catch (Exception ex)
            {
                try { File.AppendAllText("reeled_crash.log", $"[Core.Initialize Error] {ex}\n"); } catch { }
            }
        }
    }

    public LibVlcPlaybackService()
    {
        _dispatcherQueue = App.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        Task.Run(EnsureCoreInitialized);
    }

    public void InitializeEngine(string[]? swapChainOptions = null)
    {
        if (_libVLC != null) return;
        EnsureCoreInitialized();

        try
        {
            var options = new List<string>
            {
                "--no-osd",
                "--no-video-title-show",
                "--drop-late-frames",
                "--skip-frames",
                "--file-caching=300",
                "--live-caching=300",
                "--disc-caching=300",
                "--network-caching=300"
            };

            if (swapChainOptions != null && swapChainOptions.Length > 0)
            {
                options.AddRange(swapChainOptions);
            }

            _libVLC = new LibVLC(enableDebugLogs: false, options.ToArray());
            _mediaPlayer = new MediaPlayer(_libVLC);
            RegisterPlayerEvents(_mediaPlayer);
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("reeled_crash.log", $"[InitializeEngine Error] {ex}\n"); } catch { }
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            RegisterPlayerEvents(_mediaPlayer);
        }
    }

    public void AttachToVideoView(LibVLCSharp.Platforms.Windows.VideoView videoView, string[] swapChainOptions)
    {
        _dispatcherQueue ??= App.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

        if (_libVLC == null)
        {
            _currentSwapChainOptions = (string[])swapChainOptions.Clone();
            InitializeEngine(swapChainOptions);
        }

        if (_mediaPlayer != null)
        {
            if (!ReferenceEquals(videoView.MediaPlayer, _mediaPlayer))
            {
                videoView.MediaPlayer = _mediaPlayer;
            }

            if (!string.IsNullOrEmpty(_pendingFilePath))
            {
                string path = _pendingFilePath;
                _pendingFilePath = null;
                _ = PlayMediaAsync(path);
            }
        }
    }

    public void DisposeEngine()
    {
        try
        {
            Stop();
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("reeled_crash.log", $"[DisposeEngine Error] {ex}\n"); } catch { }
        }
    }

    private static bool AreOptionsEqual(string[] a, string[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private void RegisterPlayerEvents(MediaPlayer player)
    {
        player.PositionChanged += (s, e) => Dispatch(() => PositionChanged?.Invoke(e.Position));
        player.TimeChanged += (s, e) => Dispatch(() =>
        {
            if (_mediaPlayer != null && _storedPlaybackRate > 0.1f && Math.Abs(_mediaPlayer.Rate - _storedPlaybackRate) > 0.05f)
            {
                _mediaPlayer.SetRate(_storedPlaybackRate);
            }
            lock (_secondaryAudioPlayers)
            {
                foreach (var p in _secondaryAudioPlayers)
                {
                    if (p.IsPlaying && Math.Abs(p.Time - e.Time) > 250)
                    {
                        p.Time = e.Time;
                    }
                }
            }
            TimeChanged?.Invoke(e.Time);
        });
        player.LengthChanged += (s, e) => Dispatch(() => LengthChanged?.Invoke(e.Length));
        player.Playing += (s, e) => Dispatch(() =>
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = Math.Clamp(_storedVolume, 0, 150);
                _mediaPlayer.Mute = _storedMute;
                if (_storedPlaybackRate > 0.1f)
                {
                    _mediaPlayer.SetRate(_storedPlaybackRate);
                }
            }
            PlaybackStarted?.Invoke();
            AudioTracksChanged?.Invoke();
            SubtitlesChanged?.Invoke();
            Task.Delay(150).ContinueWith(_ => Dispatch(() =>
            {
                if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Volume = Math.Clamp(_storedVolume, 0, 150);
                    _mediaPlayer.Mute = _storedMute;
                    if (_storedPlaybackRate > 0.1f)
                    {
                        _mediaPlayer.SetRate(_storedPlaybackRate);
                    }
                }
            }));
            Task.Delay(300).ContinueWith(_ => Dispatch(() =>
            {
                if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
                {
                    if (_storedPlaybackRate > 0.1f && Math.Abs(_mediaPlayer.Rate - _storedPlaybackRate) > 0.05f)
                    {
                        _mediaPlayer.SetRate(_storedPlaybackRate);
                    }
                }
            }));
            Task.Delay(350).ContinueWith(_ => Dispatch(() =>
            {
                AudioTracksChanged?.Invoke();
                SubtitlesChanged?.Invoke();
            }));
        });
        player.Paused += (s, e) => Dispatch(() => PlaybackPaused?.Invoke());
        player.Stopped += (s, e) => Dispatch(() => PlaybackStopped?.Invoke());
        player.EndReached += (s, e) => Dispatch(() => MediaEnded?.Invoke());
    }

    private void Dispatch(Action action)
    {
        var dispatcher = _dispatcherQueue ?? App.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }

    public async Task PlayMediaAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        _currentFilePath = filePath;

        if (_libVLC == null || _mediaPlayer == null || _currentSwapChainOptions == null)
        {
            _pendingFilePath = filePath;
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                var oldMedia = _currentMedia;
                var newMedia = new Media(_libVLC, filePath, FromType.FromPath);
                _currentMedia = newMedia;
                _mediaPlayer.Play(newMedia);

                lock (_secondaryAudioPlayers)
                {
                    if (_activeAudioTracks.Count > 1)
                    {
                        var secondary = new List<int>();
                        for (int i = 1; i < _activeAudioTracks.Count; i++)
                        {
                            secondary.Add(_activeAudioTracks[i]);
                        }
                        Task.Delay(300).ContinueWith(_ => Dispatch(() => SyncSecondaryAudioTracks(secondary)));
                    }
                }

                if (oldMedia != null)
                {
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        try { oldMedia.Dispose(); } catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText("reeled_crash.log", $"[PlayMediaAsync Error] {ex}\n"); } catch { }
            }
        });
    }

    public void Play()
    {
        _mediaPlayer?.Play();
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.Play();
            }
        }
    }

    public void Pause()
    {
        _mediaPlayer?.SetPause(true);
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.SetPause(true);
            }
        }
    }

    public void TogglePlayPause()
    {
        if (_mediaPlayer == null) return;
        if (_mediaPlayer.IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        _pendingFilePath = null;
        StopSecondaryAudio();
        try
        {
            _mediaPlayer?.Stop();
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("reeled_crash.log", $"[Stop Error] {ex}\n"); } catch { }
        }
    }

    public void SetPosition(float ratio)
    {
        if (_mediaPlayer == null) return;
        ratio = Math.Clamp(ratio, 0.0f, 1.0f);
        _mediaPlayer.Position = ratio;
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.Position = ratio;
            }
        }
        long estimatedTime = (long)(ratio * (_mediaPlayer.Length > 0 ? _mediaPlayer.Length : 0));
        Dispatch(() =>
        {
            PositionChanged?.Invoke(ratio);
            TimeChanged?.Invoke(estimatedTime);
        });
    }

    public void SeekTime(long timeMs)
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.Time = Math.Max(0, timeMs);
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.Time = Math.Max(0, timeMs);
            }
        }
        float pos = _mediaPlayer.Length > 0 ? (float)timeMs / _mediaPlayer.Length : 0f;
        Dispatch(() =>
        {
            TimeChanged?.Invoke(timeMs);
            PositionChanged?.Invoke(pos);
        });
    }

    public void SkipSeconds(int seconds)
    {
        if (_mediaPlayer == null || _mediaPlayer.Length <= 0) return;
        long targetTime = _mediaPlayer.Time + (seconds * 1000L);
        targetTime = Math.Clamp(targetTime, 0L, _mediaPlayer.Length);
        _mediaPlayer.Time = targetTime;
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.Time = targetTime;
            }
        }
        float pos = (float)targetTime / _mediaPlayer.Length;
        Dispatch(() =>
        {
            TimeChanged?.Invoke(targetTime);
            PositionChanged?.Invoke(pos);
        });
    }

    public void SetVolume(int volume)
    {
        _storedVolume = Math.Clamp(volume, 0, 150);
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Volume = _storedVolume;
        }
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.Volume = _storedVolume;
            }
        }
    }

    public void SetMute(bool isMuted)
    {
        _storedMute = isMuted;
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Mute = _storedMute;
        }
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.Mute = _storedMute;
            }
        }
    }

    public void ToggleMute()
    {
        _storedMute = !_storedMute;
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Mute = _storedMute;
        }
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.Mute = _storedMute;
            }
        }
    }

    public void SetPlaybackRate(float rate)
    {
        _storedPlaybackRate = Math.Clamp(rate, 0.25f, 4.0f);
        if (_mediaPlayer != null)
        {
            int res = _mediaPlayer.SetRate(_storedPlaybackRate);
            if (res != 0)
            {
                Task.Run(async () =>
                {
                    for (int i = 0; i < 6; i++)
                    {
                        await Task.Delay(100);
                        if (_mediaPlayer != null && (_mediaPlayer.IsPlaying || _mediaPlayer.State == VLCState.Playing || _mediaPlayer.State == VLCState.Paused))
                        {
                            if (_mediaPlayer.SetRate(_storedPlaybackRate) == 0)
                                break;
                        }
                    }
                });
            }
        }
        lock (_secondaryAudioPlayers)
        {
            foreach (var p in _secondaryAudioPlayers)
            {
                p.SetRate(_storedPlaybackRate);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _currentMedia?.Dispose();
            _libVLC?.Dispose();
        }
        catch (Exception)
        {
        }
    }
}
