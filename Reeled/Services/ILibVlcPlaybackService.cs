using System;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace Reeled.Services;

public interface ILibVlcPlaybackService : IDisposable
{
    event Action<float>? PositionChanged;
    event Action<long>? TimeChanged;
    event Action<long>? LengthChanged;
    event Action? PlaybackStarted;
    event Action? PlaybackPaused;
    event Action? PlaybackStopped;
    event Action? MediaEnded;
    event Action? AudioTracksChanged;

    MediaPlayer? CurrentMediaPlayer { get; }
    bool IsPlaying { get; }
    float Position { get; }
    long Time { get; }
    long Length { get; }
    int Volume { get; }
    bool IsMuted { get; }
    float PlaybackRate { get; }
    int CurrentAudioTrack { get; }
    IReadOnlyList<Reeled.Models.AudioTrackInfo> GetAudioTracks();
    bool SetAudioTrack(int trackId);

    void InitializeEngine(string[]? swapChainOptions = null);
    void DisposeEngine();
    void AttachToVideoView(LibVLCSharp.Platforms.Windows.VideoView videoView, string[] swapChainOptions);
    Task PlayMediaAsync(string filePath);
    void Play();
    void Pause();
    void TogglePlayPause();
    void Stop();
    void SetPosition(float ratio);
    void SeekTime(long timeMs);
    void SkipSeconds(int seconds);
    void SetVolume(int volume);
    void SetMute(bool isMuted);
    void ToggleMute();
    void SetPlaybackRate(float rate);
}
