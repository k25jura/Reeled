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

    public MediaPlayer? CurrentMediaPlayer => _mediaPlayer;
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public float Position => _mediaPlayer?.Position ?? 0f;
    public long Time => _mediaPlayer?.Time ?? 0L;
    public long Length => _mediaPlayer?.Length ?? 0L;
    public int Volume => _mediaPlayer?.Volume ?? 100;
    public bool IsMuted => _mediaPlayer?.Mute ?? false;
    private float _storedPlaybackRate = 1.0f;
    public float PlaybackRate => _mediaPlayer != null ? _mediaPlayer.Rate : _storedPlaybackRate;
    public int CurrentAudioTrack => _mediaPlayer?.AudioTrack ?? -1;

    public IReadOnlyList<Reeled.Models.AudioTrackInfo> GetAudioTracks()
    {
        var list = new List<Reeled.Models.AudioTrackInfo>();
        if (_mediaPlayer == null) return list;

        try
        {
            var descriptions = _mediaPlayer.AudioTrackDescription;
            int current = _mediaPlayer.AudioTrack;
            if (descriptions != null)
            {
                foreach (var desc in descriptions)
                {
                    list.Add(new Reeled.Models.AudioTrackInfo
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

    public bool SetAudioTrack(int trackId)
    {
        if (_mediaPlayer == null) return false;
        try
        {
            bool success = _mediaPlayer.SetAudioTrack(trackId);
            AudioTracksChanged?.Invoke();
            return success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public LibVlcPlaybackService()
    {
        _dispatcherQueue = App.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
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
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("reeled_crash.log", $"[Core.Initialize Error] {ex}\n"); } catch { }
        }
    }

    public void InitializeEngine(string[]? swapChainOptions = null)
    {
        if (_libVLC != null) return;

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
        player.TimeChanged += (s, e) => Dispatch(() => TimeChanged?.Invoke(e.Time));
        player.LengthChanged += (s, e) => Dispatch(() => LengthChanged?.Invoke(e.Length));
        player.Playing += (s, e) => Dispatch(() =>
        {
            if (Math.Abs(_storedPlaybackRate - 1.0f) > 0.01f)
            {
                _mediaPlayer?.SetRate(_storedPlaybackRate);
            }
            PlaybackStarted?.Invoke();
            AudioTracksChanged?.Invoke();
            Task.Delay(350).ContinueWith(_ => Dispatch(() => AudioTracksChanged?.Invoke()));
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
    }

    public void Pause()
    {
        _mediaPlayer?.SetPause(true);
    }

    public void TogglePlayPause()
    {
        if (_mediaPlayer == null) return;
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.SetPause(true);
        }
        else
        {
            _mediaPlayer.Play();
        }
    }

    public void Stop()
    {
        _pendingFilePath = null;
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
        float pos = (float)targetTime / _mediaPlayer.Length;
        Dispatch(() =>
        {
            TimeChanged?.Invoke(targetTime);
            PositionChanged?.Invoke(pos);
        });
    }

    public void SetVolume(int volume)
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.Volume = Math.Clamp(volume, 0, 150);
    }

    public void SetMute(bool isMuted)
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.Mute = isMuted;
    }

    public void ToggleMute()
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.Mute = !_mediaPlayer.Mute;
    }

    public void SetPlaybackRate(float rate)
    {
        _storedPlaybackRate = rate;
        if (_mediaPlayer == null) return;
        _mediaPlayer.SetRate(rate);
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
