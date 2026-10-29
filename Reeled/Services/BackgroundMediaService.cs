using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Reeled.Helpers;
using Windows.Media.Control;

namespace Reeled.Services;

/// <summary>
/// Pauses music and other media playing in other apps (Spotify, a browser tab, …) while a clip plays, and starts
/// it again when the player stops. Works through Windows' media sessions, the same ones the volume flyout shows.
/// Only what Reeled paused is resumed: something the user paused or started themselves is left alone.
/// </summary>
public sealed class BackgroundMediaService
{
    // A clip ending and the next one loading stops playback for a moment: don't let the music back in for that
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(1.5);

    private readonly ILocalStorageService _storage;
    private readonly HashSet<string> _pausedByUs = new(StringComparer.OrdinalIgnoreCase);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private DispatcherQueueTimer? _resumeTimer;
    private bool _busy;

    /// <summary>Set once the player has created it; Reeled closing asks it to give the music back.</summary>
    public static BackgroundMediaService? Instance { get; private set; }

    public BackgroundMediaService(ILocalStorageService storage, ILibVlcPlaybackService playback)
    {
        Instance = this;
        _storage = storage;
        playback.PlaybackStarted += OnClipStarted;
        playback.PlaybackStopped += OnClipStopped;
        playback.MediaEnded += OnClipStopped;
    }

    private bool PauseEnabled => _storage.CurrentSettings.PauseOtherMedia;
    private bool ResumeEnabled => _storage.CurrentSettings.ResumeOtherMedia;

    private void OnClipStarted()
    {
        _resumeTimer?.Stop();
        if (PauseEnabled) _ = PauseOthersAsync();
    }

    private void OnClipStopped()
    {
        if (_pausedByUs.Count == 0) return;
        if (_resumeTimer == null)
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            if (queue == null)
            {
                _ = ResumeOthersAsync();
                return;
            }
            _resumeTimer = queue.CreateTimer();
            _resumeTimer.IsRepeating = false;
            _resumeTimer.Tick += (s, e) => _ = ResumeOthersAsync();
        }
        _resumeTimer.Interval = ResumeDelay;
        _resumeTimer.Start();
    }

    private async Task<GlobalSystemMediaTransportControlsSessionManager?> ManagerAsync()
    {
        try
        {
            return _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            Log.Error("BackgroundMedia.Manager", ex);
            return null;
        }
    }

    private static bool IsOwnSession(GlobalSystemMediaTransportControlsSession session) =>
        session.SourceAppUserModelId?.Contains("Reeled", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Pauses every other app that's playing right now.</summary>
    public async Task PauseOthersAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var manager = await ManagerAsync();
            if (manager == null) return;
            foreach (var session in manager.GetSessions())
            {
                try
                {
                    if (IsOwnSession(session)) continue;
                    var info = session.GetPlaybackInfo();
                    if (info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
                    if (!info.Controls.IsPauseEnabled) continue;
                    if (await session.TryPauseAsync()) _pausedByUs.Add(session.SourceAppUserModelId);
                }
                catch (Exception ex)
                {
                    Log.Error("BackgroundMedia.Pause", ex);
                }
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Reeled is closing mid-clip: resume now, waiting briefly so it happens before the process ends.</summary>
    public void ResumeBeforeExit()
    {
        if (_pausedByUs.Count == 0) return;
        _resumeTimer?.Stop();
        try
        {
            Task.Run(ResumeOthersAsync).Wait(TimeSpan.FromMilliseconds(800));
        }
        catch (Exception ex)
        {
            Log.Error("BackgroundMedia.Exit", ex);
        }
    }

    /// <summary>Plays again what <see cref="PauseOthersAsync"/> paused, if it's still paused.</summary>
    public async Task ResumeOthersAsync()
    {
        if (_pausedByUs.Count == 0) return;
        var paused = new List<string>(_pausedByUs);
        _pausedByUs.Clear();
        if (!ResumeEnabled) return;

        var manager = await ManagerAsync();
        if (manager == null) return;
        foreach (var session in manager.GetSessions())
        {
            try
            {
                if (!paused.Contains(session.SourceAppUserModelId)) continue;
                var info = session.GetPlaybackInfo();
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused && info.Controls.IsPlayEnabled)
                {
                    await session.TryPlayAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error("BackgroundMedia.Resume", ex);
            }
        }
    }
}
