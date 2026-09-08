using System;
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
    private bool _isDisposed;

    public event Action<float>? PositionChanged;
    public event Action<long>? TimeChanged;
    public event Action<long>? LengthChanged;
    public event Action? PlaybackStarted;
    public event Action? PlaybackPaused;
    public event Action? PlaybackStopped;
    public event Action? MediaEnded;

    public MediaPlayer? CurrentMediaPlayer => _mediaPlayer;
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public float Position => _mediaPlayer?.Position ?? 0f;
    public long Time => _mediaPlayer?.Time ?? 0L;
    public long Length => _mediaPlayer?.Length ?? 0L;
    public int Volume => _mediaPlayer?.Volume ?? 100;
    public bool IsMuted => _mediaPlayer?.Mute ?? false;
    public float PlaybackRate => _mediaPlayer?.Rate ?? 1.0f;

    public LibVlcPlaybackService()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        try
        {
            Core.Initialize();
        }
        catch (Exception)
        {
            // Core.Initialize might already be initialized or throw if custom path is needed
        }
    }

    public void InitializeEngine(string[]? swapChainOptions = null)
    {
        if (_libVLC != null) return;

        try
        {
            var options = swapChainOptions ?? Array.Empty<string>();
            _libVLC = new LibVLC(enableDebugLogs: false, options);
            _mediaPlayer = new MediaPlayer(_libVLC);
            RegisterPlayerEvents(_mediaPlayer);
        }
        catch (Exception)
        {
            // Fallback without extra options
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            RegisterPlayerEvents(_mediaPlayer);
        }
    }

    public void AttachToVideoView(LibVLCSharp.Platforms.Windows.VideoView videoView, string[] swapChainOptions)
    {
        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();

        if (_libVLC == null)
        {
            InitializeEngine(swapChainOptions);
        }

        if (_mediaPlayer != null)
        {
            videoView.MediaPlayer = _mediaPlayer;

            if (!string.IsNullOrEmpty(_pendingFilePath))
            {
                string path = _pendingFilePath;
                _pendingFilePath = null;
                _ = PlayMediaAsync(path);
            }
        }
    }

    private void RegisterPlayerEvents(MediaPlayer player)
    {
        player.PositionChanged += (s, e) => Dispatch(() => PositionChanged?.Invoke(e.Position));
        player.TimeChanged += (s, e) => Dispatch(() => TimeChanged?.Invoke(e.Time));
        player.LengthChanged += (s, e) => Dispatch(() => LengthChanged?.Invoke(e.Length));
        player.Playing += (s, e) => Dispatch(() => PlaybackStarted?.Invoke());
        player.Paused += (s, e) => Dispatch(() => PlaybackPaused?.Invoke());
        player.Stopped += (s, e) => Dispatch(() => PlaybackStopped?.Invoke());
        player.EndReached += (s, e) => Dispatch(() => MediaEnded?.Invoke());
    }

    private void Dispatch(Action action)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => action());
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

        if (_libVLC == null || _mediaPlayer == null)
        {
            _pendingFilePath = filePath;
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                _currentMedia?.Dispose();
                _currentMedia = new Media(_libVLC, new Uri(filePath));
                _mediaPlayer.Play(_currentMedia);
            }
            catch (Exception)
            {
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
        _mediaPlayer?.Stop();
    }

    public void SetPosition(float ratio)
    {
        if (_mediaPlayer == null) return;
        ratio = Math.Clamp(ratio, 0.0f, 1.0f);
        _mediaPlayer.Position = ratio;
    }

    public void SeekTime(long timeMs)
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.Time = Math.Max(0, timeMs);
    }

    public void SkipSeconds(int seconds)
    {
        if (_mediaPlayer == null || _mediaPlayer.Length <= 0) return;
        long targetTime = _mediaPlayer.Time + (seconds * 1000L);
        targetTime = Math.Clamp(targetTime, 0L, _mediaPlayer.Length);
        _mediaPlayer.Time = targetTime;
    }

    public void SetVolume(int volume)
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.Volume = Math.Clamp(volume, 0, 150);
    }

    public void ToggleMute()
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.Mute = !_mediaPlayer.Mute;
    }

    public void SetPlaybackRate(float rate)
    {
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
