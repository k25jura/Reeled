using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;

namespace Reeled.Services.Sharing;

/// <summary>A hardware or software H.264 encoder ffmpeg can use on this PC.</summary>
public sealed record VideoEncoder(string Codec, string DisplayName, bool IsHardware);

/// <summary>
/// Makes a clip small enough to share: one H.264 pass at the bitrate that fits the size limit (with room
/// for the container), on the graphics card when it can. Lower bitrates also get a lower resolution and,
/// at the very low end, 30 fps, which looks far better than starving 1440p60 of bits. When the result
/// still comes out too big it is encoded once more at a proportionally lower bitrate.
/// </summary>
public static class ClipCompressor
{
    private static readonly VideoEncoder Software = new("libx264", "x264 (CPU)", false);
    private static readonly VideoEncoder[] HardwareEncoders =
    {
        new("h264_nvenc", "NVIDIA NVENC", true),
        new("h264_amf", "AMD AMF", true),
        new("h264_qsv", "Intel Quick Sync", true)
    };

    private static VideoEncoder? _encoder;

    /// <summary>The detected encoder is re-checked after ffmpeg changes.</summary>
    public static void ForgetEncoder() => _encoder = null;

    /// <summary>The first hardware encoder that really encodes a test frame, or x264.</summary>
    public static async Task<VideoEncoder> DetectEncoderAsync(bool allowHardware, CancellationToken token = default)
    {
        if (!allowHardware) return Software;
        if (_encoder != null) return _encoder;
        var ffmpeg = FfmpegLocator.Find();
        if (ffmpeg == null) return Software;

        foreach (var candidate in HardwareEncoders)
        {
            // Listed encoders aren't necessarily usable (no such GPU, old driver), so encode a few frames
            if (await TryEncodeAsync(ffmpeg, candidate.Codec, token))
            {
                _encoder = candidate;
                return candidate;
            }
        }
        _encoder = Software;
        return Software;
    }

    private static async Task<bool> TryEncodeAsync(string ffmpeg, string codec, CancellationToken token)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg,
                $"-hide_banner -loglevel error -f lavfi -i color=c=black:s=640x360:r=30 -frames:v 5 -c:v {codec} -f null -")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            _ = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Encoding settings: bitrates, the tallest output (0 = as the source) and a frame-rate cap (0 = none).</summary>
    public sealed record Plan(int VideoKbps, int AudioKbps, int MaxHeight, int FpsCap);

    public static Plan PlanFor(TimeSpan duration, long targetBytes, int sourceHeight)
    {
        double seconds = Math.Max(1, duration.TotalSeconds);
        // ~5% for the MP4 container and rate-control wobble
        double totalKbps = targetBytes * 8 / 1000.0 / seconds * 0.95;
        int audio = totalKbps < 400 ? 64 : totalKbps < 1200 ? 96 : 128;
        int video = (int)Math.Max(120, totalKbps - audio);

        // Height that still looks right at this bitrate (H.264, game footage)
        int height = video >= 9000 ? 1440 : video >= 4500 ? 1080 : video >= 2200 ? 720 : video >= 1100 ? 540 : 480;
        if (sourceHeight > 0) height = Math.Min(height, sourceHeight);
        return new Plan(video, audio, height, video < 1100 ? 30 : 0);
    }

    public sealed record Result(string Path, long Bytes, VideoEncoder Encoder);

    /// <summary>
    /// Encodes <paramref name="input"/> (optionally only start..start+length) into an MP4 that fits
    /// <paramref name="targetBytes"/>. Progress is 0..1 across both attempts.
    /// </summary>
    public static async Task<Result> CompressAsync(string input, string output, TimeSpan clipDuration, int sourceHeight, double sourceFps,
        TimeSpan? start, TimeSpan? length, long targetBytes, bool allowHardware,
        IProgress<double> progress, CancellationToken token)
    {
        var ffmpeg = FfmpegLocator.Find() ?? throw new InvalidOperationException("FFmpeg isn't installed.");
        var encoder = await DetectEncoderAsync(allowHardware, token);
        if (clipDuration <= TimeSpan.Zero) clipDuration = await ProbeDurationAsync(ffmpeg, input, token);
        TimeSpan duration = length ?? (clipDuration - (start ?? TimeSpan.Zero));
        if (duration <= TimeSpan.Zero) duration = clipDuration;

        var plan = PlanFor(duration, targetBytes, sourceHeight);
        // Only slow down sources faster than 30 fps (or unknown ones)
        if (sourceFps is > 0 and <= 31) plan = plan with { FpsCap = 0 };
        await EncodeAsync(ffmpeg, encoder, input, output, start, length, duration, plan, p => progress.Report(p * 0.85), token);

        long bytes = new FileInfo(output).Length;
        if (bytes > targetBytes)
        {
            // Rate control overshot (very busy footage): once more, scaled down with a little extra margin
            double scale = targetBytes / (double)bytes * 0.92;
            var retry = plan with { VideoKbps = Math.Max(100, (int)(plan.VideoKbps * scale)) };
            await EncodeAsync(ffmpeg, encoder, input, output, start, length, duration, retry, p => progress.Report(0.85 + p * 0.15), token);
            bytes = new FileInfo(output).Length;
        }
        progress.Report(1);
        return new Result(output, bytes, encoder);
    }

    /// <summary>
    /// Encodes at the user's own settings (Custom quality): no size target, so one pass. A frame-rate cap
    /// at or above the source's is ignored, and the height only ever scales down.
    /// </summary>
    public static async Task<Result> CompressCustomAsync(string input, string output, TimeSpan clipDuration, double sourceFps,
        TimeSpan? start, TimeSpan? length, int height, int fps, int videoKbps, bool allowHardware,
        IProgress<double> progress, CancellationToken token)
    {
        var ffmpeg = FfmpegLocator.Find() ?? throw new InvalidOperationException("FFmpeg isn't installed.");
        var encoder = await DetectEncoderAsync(allowHardware, token);
        if (clipDuration <= TimeSpan.Zero) clipDuration = await ProbeDurationAsync(ffmpeg, input, token);
        TimeSpan duration = length ?? (clipDuration - (start ?? TimeSpan.Zero));
        if (duration <= TimeSpan.Zero) duration = clipDuration;

        int fpsCap = fps > 0 && (sourceFps <= 0 || fps < sourceFps - 0.5) ? fps : 0;
        int audio = videoKbps < 1500 ? 96 : 160;
        var plan = new Plan(Math.Clamp(videoKbps, 100, 200_000), audio, Math.Max(0, height), fpsCap);
        await EncodeAsync(ffmpeg, encoder, input, output, start, length, duration, plan, p => progress.Report(p), token);
        progress.Report(1);
        return new Result(output, new FileInfo(output).Length, encoder);
    }

    /// <summary>Roughly how big a Custom encode of this length comes out, in bytes.</summary>
    public static long EstimateCustomBytes(TimeSpan duration, int videoKbps)
    {
        int audio = videoKbps < 1500 ? 96 : 160;
        return (long)((videoKbps + audio) * 1000 / 8.0 * Math.Max(1, duration.TotalSeconds) * 1.02);
    }

    /// <summary>Cuts a range without re-encoding (Original quality with a loop set).</summary>
    public static async Task TrimAsync(string input, string output, TimeSpan start, TimeSpan length,
        IProgress<double> progress, CancellationToken token)
    {
        var ffmpeg = FfmpegLocator.Find() ?? throw new InvalidOperationException("FFmpeg isn't installed.");
        await FFMpegArguments
            .FromFileInput(input, true, o => o.Seek(start))
            .OutputToFile(output, true, o => o
                .WithDuration(length)
                .WithCustomArgument("-map 0:v:0 -map 0:a? -c copy -avoid_negative_ts make_zero -movflags +faststart"))
            .NotifyOnProgress(p => progress.Report(Math.Clamp(p / 100, 0, 1)), length)
            .CancellableThrough(token, 3000)
            .Configure(o => o.BinaryFolder = Path.GetDirectoryName(ffmpeg)!)
            .ProcessAsynchronously();
        token.ThrowIfCancellationRequested();
    }

    /// <summary>The file's length from ffprobe, for clips whose metadata hasn't been read yet.</summary>
    private static async Task<TimeSpan> ProbeDurationAsync(string ffmpeg, string input, CancellationToken token)
    {
        string probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(probe)) return TimeSpan.Zero;
        try
        {
            var analysis = await FFProbe.AnalyseAsync(input, new FFOptions { BinaryFolder = Path.GetDirectoryName(ffmpeg)! }, token);
            return analysis.Duration;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Helpers.Log.Error("Share.Probe", ex);
            return TimeSpan.Zero;
        }
    }

    /// <summary>How many audio tracks the file has (game and microphone are often separate), via ffprobe.</summary>
    private static async Task<int> CountAudioTracksAsync(string ffmpeg, string input, CancellationToken token)
    {
        string probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(probe)) return 1;
        try
        {
            var analysis = await FFProbe.AnalyseAsync(input, new FFOptions { BinaryFolder = Path.GetDirectoryName(ffmpeg)! }, token);
            return analysis.AudioStreams.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Helpers.Log.Error("Share.Probe", ex);
            return 1;
        }
    }

    private static async Task EncodeAsync(string ffmpeg, VideoEncoder encoder, string input, string output,
        TimeSpan? start, TimeSpan? length, TimeSpan duration, Plan plan, Action<double> progress, CancellationToken token)
    {
        int audioTracks = await CountAudioTracksAsync(ffmpeg, input, token);
        try
        {
            await EncodeOnceAsync(ffmpeg, encoder, input, output, start, length, duration, plan, audioTracks, gpuDecode: true, progress, token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OperationCanceledException)
        {
            // GPU decoding doesn't pair with every encoder (AMF can't open on another adapter's device on
            // hybrid laptops); decoding on the CPU always works, just slower
            Helpers.Log.Error("Share.Encode (GPU decode)", ex.Message);
            await EncodeOnceAsync(ffmpeg, encoder, input, output, start, length, duration, plan, audioTracks, gpuDecode: false, progress, token);
        }
    }

    private static async Task EncodeOnceAsync(string ffmpeg, VideoEncoder encoder, string input, string output,
        TimeSpan? start, TimeSpan? length, TimeSpan duration, Plan plan, int audioTracks, bool gpuDecode,
        Action<double> progress, CancellationToken token)
    {
        int v = plan.VideoKbps;
        string rate = encoder.Codec switch
        {
            "h264_nvenc" => $"-preset p5 -rc cbr -b:v {v}k -maxrate {v}k -bufsize {v * 2}k",
            "h264_amf" => $"-quality balanced -rc cbr -b:v {v}k -maxrate {v}k -bufsize {v * 2}k",
            "h264_qsv" => $"-preset medium -b:v {v}k -maxrate {v}k -bufsize {v * 2}k",
            _ => $"-preset veryfast -b:v {v}k -maxrate {v}k -bufsize {v * 2}k"
        };
        // Scale only down, keep the aspect ratio, even width for 4:2:0
        var videoFilters = new System.Collections.Generic.List<string>();
        if (plan.MaxHeight > 0) videoFilters.Add($"scale=-2:'min({plan.MaxHeight},ih)'");
        if (plan.FpsCap > 0) videoFilters.Add($"fps={plan.FpsCap}");
        string video = "[0:v:0]" + (videoFilters.Count > 0 ? string.Join(",", videoFilters) : "null") + "[v]";
        // Every audio track is heard, mixed at its own level (game + microphone, as recorded)
        string graph, audioMap;
        if (audioTracks > 1)
        {
            string inputs = string.Concat(Enumerable.Range(0, audioTracks).Select(i => $"[0:a:{i}]"));
            graph = $"{video};{inputs}amix=inputs={audioTracks}:normalize=0:dropout_transition=0[a]";
            audioMap = "-map \"[a]\"";
        }
        else
        {
            graph = video;
            audioMap = audioTracks == 1 ? "-map 0:a:0" : string.Empty;
        }
        string args = $"-filter_complex \"{graph}\" -map \"[v]\" {audioMap} -c:v {encoder.Codec} {rate} -pix_fmt yuv420p " +
                      (audioTracks > 0 ? $"-c:a aac -b:a {plan.AudioKbps}k -ac 2 " : string.Empty) + "-movflags +faststart";

        await FFMpegArguments
            .FromFileInput(input, true, o =>
            {
                // D3D11 decoding works on any GPU vendor and halves the time for AV1/HEVC 1440p sources
                if (gpuDecode) o.WithCustomArgument("-hwaccel d3d11va");
                if (start is TimeSpan s && s > TimeSpan.Zero) o.Seek(s);
            })
            .OutputToFile(output, true, o =>
            {
                if (length is TimeSpan l) o.WithDuration(l);
                o.WithCustomArgument(args);
            })
            .NotifyOnProgress(p => progress(Math.Clamp(p / 100, 0, 1)), duration)
            .CancellableThrough(token, 3000)
            .Configure(o => o.BinaryFolder = Path.GetDirectoryName(ffmpeg)!)
            .ProcessAsynchronously();
        token.ThrowIfCancellationRequested();
    }
}
