using System;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace Reeled.Services;

public interface ILibVlcPlaybackService : IDisposable
{
    /// <summary>LibVLC version string, or null before the engine has started.</summary>
    string? EngineVersion { get; }
    event Action<float>? PositionChanged;
    event Action<long>? TimeChanged;
    event Action<long>? LengthChanged;
    event Action? PlaybackStarted;
    event Action? PlaybackPaused;
    event Action? PlaybackStopped;
    event Action? MediaEnded;
    event Action? AudioTracksChanged;
    event Action? SubtitlesChanged;

    MediaPlayer? CurrentMediaPlayer { get; }
    bool IsPlaying { get; }
    float Position { get; }
    long Time { get; }
    long Length { get; }
    int Volume { get; }
    bool IsMuted { get; }
    float PlaybackRate { get; }
    int CurrentAudioTrack { get; }
    IReadOnlyList<int> ActiveAudioTracks { get; }
    IReadOnlyList<Reeled.Models.AudioTrackInfo> GetAudioTracks();
    bool SetAudioTrack(int trackId);
    bool SetAudioTracks(IEnumerable<int> trackIds);

    int CurrentSubtitleTrack { get; }
    IReadOnlyList<Reeled.Models.SubtitleTrackInfo> GetSubtitleTracks();
    bool SetSubtitleTrack(int trackId);
    bool AddSubtitleFile(string filePath);
    void SetSubtitleDelay(long delayMs);

    void DisposeEngine();
    /// <summary>Starts the engine for this swap chain off the UI thread, then hands it to the view.</summary>
    Task AttachToVideoViewAsync(LibVLCSharp.Platforms.Windows.VideoView videoView, string[] swapChainOptions);
    float Fps { get; }

    /// <summary>Loads and plays a file. Overlapping calls are serialized and only the latest one wins.</summary>
    Task PlayMediaAsync(string filePath, long startTimeMs = 0);
    void Play();
    void Pause();
    void TogglePlayPause();
    void Stop();
    /// <summary>Stops playback without blocking the UI thread on video output teardown.</summary>
    Task StopAsync();
    /// <summary>Advances exactly one frame and leaves playback paused.</summary>
    void NextFrame();
    void SetPosition(float ratio);
    void SeekTime(long timeMs);
    void SkipSeconds(int seconds);
    void SetVolume(int volume);
    void SetMute(bool isMuted);
    void ToggleMute();
    void SetPlaybackRate(float rate);

    // Session effects (LibVlcPlaybackService.Effects.cs)
    Reeled.Models.VideoAdjustments VideoAdjust { get; }
    string? AspectRatio { get; }
    long AudioDelayMs { get; }
    long SubtitleDelayMs { get; }
    void SetVideoAdjustments(Reeled.Models.VideoAdjustments adjust);
    void SetAspectRatio(string? ratio);
    int EqualizerPresetCount { get; }
    string EqualizerPresetName(int index);
    float[] EqualizerBandFrequencies();
    (float[] Bands, float Preamp) EqualizerPreset(int index);
    void SetEqualizer(float[]? bands, float preamp);
    void SetAudioDelay(long delayMs);
    IReadOnlyList<(string Id, string Name)> GetAudioOutputDevices();
    string? CurrentAudioOutputDevice { get; }
    bool SetAudioOutputDevice(string deviceId);
    Task<string?> TakeSnapshotAsync(string folder, string baseName);
    Reeled.Models.SubtitleStyle SubtitleStyle { get; }
    void SetSubtitleStyle(Reeled.Models.SubtitleStyle style);
}
