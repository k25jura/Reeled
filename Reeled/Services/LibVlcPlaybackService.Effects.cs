using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Reeled.Models;

namespace Reeled.Services;

/// <summary>
/// Session effects: picture adjustments, aspect ratio, equalizer, audio / subtitle sync, output device,
/// snapshots and subtitle styling. Values are kept here and re-applied whenever a clip starts, because
/// libvlc resets some of them per media.
/// </summary>
public partial class LibVlcPlaybackService
{
    private VideoAdjustments _videoAdjust = new();
    private string? _aspectRatio;
    private float[]? _eqBands;
    private float _eqPreamp;
    private long _audioDelayMs;
    private long _subtitleDelayMs;
    private SubtitleStyle _subtitleStyle = new();

    public VideoAdjustments VideoAdjust => _videoAdjust;
    public string? AspectRatio => _aspectRatio;
    public long AudioDelayMs => _audioDelayMs;
    public long SubtitleDelayMs => _subtitleDelayMs;

    public void SetVideoAdjustments(VideoAdjustments adjust)
    {
        _videoAdjust = adjust.Clone();
        ApplyVideoAdjust();
    }

    private void ApplyVideoAdjust()
    {
        var p = _mediaPlayer;
        if (p == null) return;
        try
        {
            if (_videoAdjust.IsDefault)
            {
                p.SetAdjustInt(VideoAdjustOption.Enable, 0);
                return;
            }
            p.SetAdjustInt(VideoAdjustOption.Enable, 1);
            p.SetAdjustFloat(VideoAdjustOption.Brightness, (float)_videoAdjust.Brightness);
            p.SetAdjustFloat(VideoAdjustOption.Contrast, (float)_videoAdjust.Contrast);
            p.SetAdjustFloat(VideoAdjustOption.Saturation, (float)_videoAdjust.Saturation);
            p.SetAdjustFloat(VideoAdjustOption.Gamma, (float)_videoAdjust.Gamma);
            p.SetAdjustFloat(VideoAdjustOption.Hue, (float)_videoAdjust.Hue);
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("VideoAdjust", ex);
        }
    }

    /// <summary>null = the file's own ratio; otherwise "16:9", "4:3"...</summary>
    public void SetAspectRatio(string? ratio)
    {
        _aspectRatio = string.IsNullOrWhiteSpace(ratio) ? null : ratio;
        try
        {
            if (_mediaPlayer != null) _mediaPlayer.AspectRatio = _aspectRatio;
        }
        catch { }
    }

    public int EqualizerPresetCount
    {
        get { try { using var eq = new Equalizer(); return (int)eq.PresetCount; } catch { return 0; } }
    }

    public string EqualizerPresetName(int index)
    {
        try { using var eq = new Equalizer(); return eq.PresetName((uint)index) ?? string.Empty; } catch { return string.Empty; }
    }

    public float[] EqualizerBandFrequencies()
    {
        try
        {
            using var eq = new Equalizer();
            var result = new float[eq.BandCount];
            for (uint i = 0; i < eq.BandCount; i++) result[i] = eq.BandFrequency(i);
            return result;
        }
        catch { return Array.Empty<float>(); }
    }

    public (float[] Bands, float Preamp) EqualizerPreset(int index)
    {
        try
        {
            using var eq = new Equalizer((uint)index);
            var bands = new float[eq.BandCount];
            for (uint i = 0; i < eq.BandCount; i++) bands[i] = eq.Amp(i);
            return (bands, eq.Preamp);
        }
        catch { return (new float[10], 0); }
    }

    /// <summary>null bands turn the equalizer off.</summary>
    public void SetEqualizer(float[]? bands, float preamp)
    {
        _eqBands = bands == null ? null : (float[])bands.Clone();
        _eqPreamp = preamp;
        ApplyEqualizer();
    }

    private void ApplyEqualizer()
    {
        foreach (var p in AllPlayers())
        {
            try
            {
                if (_eqBands == null)
                {
                    p.UnsetEqualizer();
                    continue;
                }
                using var eq = new Equalizer();
                eq.SetPreamp(_eqPreamp);
                for (uint i = 0; i < eq.BandCount && i < _eqBands.Length; i++) eq.SetAmp(_eqBands[i], i);
                p.SetEqualizer(eq);
            }
            catch (Exception ex)
            {
                Helpers.Log.Error("Equalizer", ex);
            }
        }
    }

    public void SetAudioDelay(long delayMs)
    {
        _audioDelayMs = delayMs;
        foreach (var p in AllPlayers())
        {
            try { p.SetAudioDelay(delayMs * 1000L); } catch { }
        }
    }

    private IEnumerable<MediaPlayer> AllPlayers()
    {
        var list = new List<MediaPlayer>();
        if (_mediaPlayer != null) list.Add(_mediaPlayer);
        lock (_secondaryAudioPlayers) list.AddRange(_secondaryAudioPlayers);
        return list;
    }

    public IReadOnlyList<(string Id, string Name)> GetAudioOutputDevices()
    {
        var result = new List<(string, string)>();
        try
        {
            var devices = _mediaPlayer?.AudioOutputDeviceEnum;
            if (devices == null) return result;
            foreach (var d in devices)
            {
                if (string.IsNullOrEmpty(d.DeviceIdentifier) && string.IsNullOrEmpty(d.Description)) continue;
                result.Add((d.DeviceIdentifier ?? string.Empty, d.Description ?? d.DeviceIdentifier ?? string.Empty));
            }
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("AudioDevices", ex);
        }
        return result;
    }

    public string? CurrentAudioOutputDevice
    {
        get { try { return _mediaPlayer?.OutputDevice; } catch { return null; } }
    }

    public bool SetAudioOutputDevice(string deviceId)
    {
        bool ok = false;
        foreach (var p in AllPlayers())
        {
            try { p.SetOutputDevice(deviceId); ok = true; } catch { }
        }
        return ok;
    }

    /// <summary>Saves the current frame as a PNG in the given folder; returns its path.</summary>
    public async Task<string?> TakeSnapshotAsync(string folder, string baseName)
    {
        var p = _mediaPlayer;
        if (p == null) return null;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (char c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
            string path = Path.Combine(folder, $"{baseName} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");
            bool ok = await Task.Run(() => p.TakeSnapshot(0, path, 0, 0));
            // libvlc writes the file asynchronously; give it a moment
            for (int i = 0; i < 20 && !File.Exists(path); i++) await Task.Delay(50);
            return ok && File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("Snapshot", ex);
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Subtitle style: libvlc 3 only reads the text renderer's settings when a clip opens, so the style
    // goes into the media options and applies from the next clip (or a reload of the current one).
    // ---------------------------------------------------------------------------------------------

    public SubtitleStyle SubtitleStyle => _subtitleStyle;

    public void SetSubtitleStyle(SubtitleStyle style) => _subtitleStyle = style.Clone();

    private void AddSubtitleStyleOptions(Media media)
    {
        var s = _subtitleStyle;
        if (s.IsDefault) return;
        try
        {
            // Relative font size: libvlc's default is ~1/20 of the video height (rel-fontsize 20 = smaller)
            int rel = (int)Math.Round(20 / Math.Clamp(s.Scale, 0.4, 3.0));
            media.AddOption($":freetype-rel-fontsize={Math.Clamp(rel, 6, 50)}");
            media.AddOption($":freetype-color={s.ColorRgb}");
            media.AddOption($":freetype-bold={(s.Bold ? 1 : 0)}");
            media.AddOption($":freetype-outline-thickness={(s.Background == SubtitleBackground.Outline ? 4 : s.Background == SubtitleBackground.None ? 0 : 2)}");
            if (s.Background == SubtitleBackground.Box)
            {
                media.AddOption(":freetype-background-opacity=170");
                media.AddOption(":freetype-background-color=0");
            }
        }
        catch { }
    }

    /// <summary>Re-applies everything after a clip starts (some settings reset per media).</summary>
    private void ReapplySessionEffects()
    {
        ApplyVideoAdjust();
        try { if (_mediaPlayer != null && _aspectRatio != null) _mediaPlayer.AspectRatio = _aspectRatio; } catch { }
        if (_eqBands != null) ApplyEqualizer();
        if (_audioDelayMs != 0) SetAudioDelay(_audioDelayMs);
        if (_subtitleDelayMs != 0)
        {
            try { _mediaPlayer?.SetSpuDelay(_subtitleDelayMs * 1000L); } catch { }
        }
    }
}
