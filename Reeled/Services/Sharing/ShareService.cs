using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Reeled.Models;

namespace Reeled.Services.Sharing;

public enum ShareStage { Preparing, Compressing, Trimming, Uploading, Done, Failed, Cancelled }

/// <summary>What to share: a clip (or the part start..start+length of it), how, and where.</summary>
public sealed record ShareRequest(GameClip Clip, ShareQuality Quality, ShareDestination Destination, TimeSpan? Start = null, TimeSpan? Length = null);

/// <summary>One share in progress or finished. Created on the UI thread, so its progress updates arrive there.</summary>
public sealed partial class ShareJob : ObservableObject
{
    public ShareRequest Request { get; }
    public GameClip Clip => Request.Clip;

    [ObservableProperty] private ShareStage _stage = ShareStage.Preparing;
    /// <summary>0..1 across the whole job.</summary>
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string? _link;
    /// <summary>The file that was (or would be) uploaded.</summary>
    [ObservableProperty] private string? _outputPath;
    [ObservableProperty] private long _outputBytes;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _linkCopied;
    [ObservableProperty] private bool _fileCopied;

    public bool IsRunning => Stage is ShareStage.Preparing or ShareStage.Compressing or ShareStage.Trimming or ShareStage.Uploading;

    internal readonly CancellationTokenSource Cancellation = new();

    public ShareJob(ShareRequest request) => Request = request;

    partial void OnStageChanged(ShareStage value) => OnPropertyChanged(nameof(IsRunning));
}

/// <summary>
/// Runs shares one at a time: compress (or cut) with FFmpeg when needed, upload, then put the link (or the
/// file) on the clipboard. The job outlives the dialog, so closing it doesn't stop a long upload; whoever is
/// showing UI hears about the result through <see cref="JobFinished"/>.
/// </summary>
public sealed class ShareService
{
    private readonly ILocalStorageService _storage;
    private readonly ILocalizationService _loc;

    public ShareJob? Current { get; private set; }

    /// <summary>Raised on the UI thread when a job ends (done, failed or cancelled).</summary>
    public event Action<ShareJob>? JobFinished;

    /// <summary>The share history changed (on the UI thread); carries the clip's path, or null for many clips.</summary>
    public event Action<string?>? HistoryChanged;

    public static string WorkFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reeled", "Shared");

    public ShareService(ILocalStorageService storage, ILocalizationService loc)
    {
        _storage = storage;
        _loc = loc;
        var known = History.Where(r => r.CopyPath != null).Select(r => r.CopyPath!).ToList();
        _ = Task.Run(() => CleanOrphans(known));
    }

    private SharePreferences Prefs => _storage.CurrentSettings.Share ??= new SharePreferences();

    /// <summary>Every share, newest first.</summary>
    public List<SharedLink> History => _storage.CurrentSettings.SharedLinks ??= new List<SharedLink>();

    /// <summary>
    /// Copies are kept as long as their share is in the history (Storage can delete them all); files no share
    /// points at (an interrupted run, a removed entry) go after a day.
    /// </summary>
    private static void CleanOrphans(List<string> known)
    {
        try
        {
            if (!Directory.Exists(WorkFolder)) return;
            var keep = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(WorkFolder))
            {
                if (!keep.Contains(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(1))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>Whether this request needs FFmpeg (compressing, cutting a range, or custom settings).</summary>
    public static bool NeedsFfmpeg(ShareQuality quality, bool range) => quality != ShareQuality.Original || range;

    public List<SharedLink> SharesOf(string clipPath) =>
        History.Where(r => string.Equals(r.ClipPath, clipPath, StringComparison.OrdinalIgnoreCase)).ToList();

    public int CountFor(string clipPath) =>
        History.Count(r => string.Equals(r.ClipPath, clipPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Takes a share off the history, and its copy off the disk.</summary>
    public void Remove(SharedLink record)
    {
        DeleteCopyFile(record);
        History.Remove(record);
        Save(record.ClipPath);
    }

    /// <summary>Deletes a share's compressed copy; the link stays in the history.</summary>
    public void DeleteCopy(SharedLink record)
    {
        DeleteCopyFile(record);
        Save(record.ClipPath);
    }

    /// <summary>Number and size of the compressed copies on disk.</summary>
    public static (int Files, long Bytes) CopyStats()
    {
        try
        {
            if (!Directory.Exists(WorkFolder)) return (0, 0);
            var files = new DirectoryInfo(WorkFolder).GetFiles();
            return (files.Length, files.Sum(f => f.Length));
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>Frees the space: every compressed copy goes, links stay in the history.</summary>
    public void DeleteAllCopies()
    {
        foreach (var record in History) record.CopyPath = null;
        try
        {
            if (Directory.Exists(WorkFolder))
            {
                foreach (var file in Directory.EnumerateFiles(WorkFolder))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
        Save(null);
    }

    /// <summary>Forgets every share (and deletes the copies); links already given out keep working.</summary>
    public void ClearHistory()
    {
        DeleteAllCopies();
        History.Clear();
        Save(null);
    }

    private static void DeleteCopyFile(SharedLink record)
    {
        if (record.CopyPath == null) return;
        try { if (File.Exists(record.CopyPath)) File.Delete(record.CopyPath); } catch { }
        record.CopyPath = null;
    }

    private void Save(string? clipPath)
    {
        _ = _storage.SaveSettingsAsync(_storage.CurrentSettings);
        HistoryChanged?.Invoke(clipPath);
    }

    /// <summary>Adds a finished share to the history, newest first.</summary>
    private void Record(ShareJob job, string source)
    {
        var request = job.Request;
        DateTime now = DateTime.UtcNow;
        DateTime? expires = null;
        if (request.Destination == ShareDestination.Litterbox &&
            int.TryParse(Prefs.LitterboxExpiry.TrimEnd('h'), out int hours))
        {
            expires = now.AddHours(hours);
        }
        bool isCopy = job.OutputPath != null && !string.Equals(job.OutputPath, source, StringComparison.OrdinalIgnoreCase);
        History.Insert(0, new SharedLink
        {
            ClipPath = source,
            Link = job.Link,
            Destination = request.Destination,
            Quality = request.Quality,
            SharedUtc = now,
            ExpiresUtc = expires,
            CopyPath = isCopy ? job.OutputPath : null,
            Bytes = job.OutputBytes,
            StartMs = request.Start is TimeSpan s ? (long)s.TotalMilliseconds : null,
            LengthMs = request.Length is TimeSpan l ? (long)l.TotalMilliseconds : null
        });
        Save(source);
    }

    public ShareJob Start(ShareRequest request)
    {
        if (Current?.IsRunning == true) throw new InvalidOperationException("A clip is already being shared.");
        var job = new ShareJob(request);
        Current = job;
        _ = RunAsync(job);
        return job;
    }

    public void Cancel() => Current?.Cancellation.Cancel();

    private async Task RunAsync(ShareJob job)
    {
        var request = job.Request;
        var token = job.Cancellation.Token;
        var prefs = Prefs;
        bool upload = request.Destination != ShareDestination.FileOnly;
        bool range = request.Start != null && request.Length != null;
        // Weight of the FFmpeg part of the bar when an upload follows
        double prepWeight = upload ? 0.6 : 1.0;

        string? workFile = null;
        try
        {
            string source = request.Clip.FilePath;
            if (!File.Exists(source)) throw new ShareUploadException(_loc["Share_Error_Missing"]);
            string fileToShare = source;

            // A whole clip that already fits goes as it is: re-encoding would only lose quality
            bool alreadySmall = !range && new FileInfo(source).Length <= prefs.TargetSizeMb * 1024L * 1024L;
            if (request.Quality == ShareQuality.Custom || (request.Quality == ShareQuality.Compressed && !alreadySmall))
            {
                Directory.CreateDirectory(WorkFolder);
                string output = Path.Combine(WorkFolder, CopyName(request.Clip, ".mp4"));
                workFile = output;
                var encoder = await ClipCompressor.DetectEncoderAsync(prefs.UseHardwareEncoder, token);
                job.Stage = ShareStage.Compressing;
                string label = string.Format(_loc["Share_Status_Compressing"], encoder.DisplayName);
                job.Status = label;
                var progress = new Progress<double>(p =>
                {
                    job.Progress = p * prepWeight;
                    job.Status = $"{label}  ·  {p:P0}";
                });
                var result = request.Quality == ShareQuality.Custom
                    ? await ClipCompressor.CompressCustomAsync(source, output, request.Clip.Duration, request.Clip.Framerate,
                        request.Start, request.Length, prefs.CustomHeight, prefs.CustomFps,
                        ClipCompressor.CustomKbps(request.Clip.VideoWidth, request.Clip.VideoHeight, request.Clip.Framerate,
                            prefs.CustomHeight, prefs.CustomFps, prefs.CustomVideoKbps),
                        prefs.UseHardwareEncoder,
                        progress, token)
                    : await ClipCompressor.CompressAsync(source, output, request.Clip.Duration, (int)request.Clip.VideoHeight,
                        request.Clip.Framerate, request.Start, request.Length, prefs.TargetSizeMb * 1024L * 1024L, prefs.UseHardwareEncoder,
                        progress, token);
                fileToShare = result.Path;
            }
            else if (range)
            {
                Directory.CreateDirectory(WorkFolder);
                string output = Path.Combine(WorkFolder, CopyName(request.Clip, Path.GetExtension(source)));
                workFile = output;
                job.Stage = ShareStage.Trimming;
                job.Status = _loc["Share_Status_Trimming"];
                var progress = new Progress<double>(p => job.Progress = p * prepWeight);
                await ClipCompressor.TrimAsync(source, output, request.Start!.Value, request.Length!.Value, progress, token);
                fileToShare = output;
            }

            job.OutputPath = fileToShare;
            job.OutputBytes = new FileInfo(fileToShare).Length;

            if (upload)
            {
                job.Stage = ShareStage.Uploading;
                string host = ShareUploader.HostName(request.Destination);
                double start = fileToShare == source ? 0 : prepWeight;
                long total = job.OutputBytes;
                var progress = new Progress<double>(p =>
                {
                    job.Progress = start + p * (1 - start);
                    job.Status = string.Format(_loc["Share_Status_Uploading"], host, FormatMb(total * p), FormatMb(total));
                });
                job.Status = string.Format(_loc["Share_Status_Uploading"], host, FormatMb(0), FormatMb(total));
                string? key = request.Destination == ShareDestination.PixelDrain
                    ? GameIconCredentials.Unprotect(prefs.ProtectedPixelDrainKey)
                    : null;
                job.Link = await ShareUploader.UploadAsync(request.Destination, fileToShare, prefs.LitterboxExpiry, key, progress, token);
                if (prefs.CopyLinkAutomatically) job.LinkCopied = CopyText(job.Link);
                job.Status = _loc[job.LinkCopied ? "Share_Status_DoneCopied" : "Share_Status_Done"];
            }
            else
            {
                job.FileCopied = await CopyFileAsync(fileToShare);
                job.Status = _loc[job.FileCopied ? "Share_Status_FileCopied" : "Share_Status_FileReady"];
            }

            job.Progress = 1;
            job.Stage = ShareStage.Done;
            Record(job, source);
        }
        catch (Exception ex) when (token.IsCancellationRequested || ex is OperationCanceledException)
        {
            job.Stage = ShareStage.Cancelled;
            job.Status = _loc["Share_Status_Cancelled"];
        }
        catch (ShareUploadException ex)
        {
            job.Error = ex.Message;
            job.Status = ex.Message;
            job.Stage = ShareStage.Failed;
            Helpers.Log.Error("Share", ex.InnerException ?? ex);
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("Share", ex);
            job.Error = string.Format(_loc["Share_Error_Generic"], FirstLine(ex.Message));
            job.Status = job.Error;
            job.Stage = ShareStage.Failed;
        }

        // A half-written file from a failed or cancelled share is of no use
        if (job.Stage != ShareStage.Done && workFile != null)
        {
            try { File.Delete(workFile); } catch { }
        }
        JobFinished?.Invoke(job);
    }

    /// <summary>FFmpeg failures carry its whole log; the dialog only needs the first line.</summary>
    private static string FirstLine(string message)
    {
        string line = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? message;
        return line.Length > 160 ? line[..160] + "…" : line;
    }

    private static string FormatMb(double bytes) => (bytes / (1024 * 1024)).ToString("0.0");

    /// <summary>A copy's name: the clip's name and when it was made, so shares of one clip don't overwrite each other.</summary>
    private static string CopyName(GameClip clip, string extension) =>
        $"{SafeName(clip)} {DateTime.Now:yyyy-MM-dd HH.mm.ss}{extension}";

    /// <summary>The clip's name made safe for a file name and a URL.</summary>
    private static string SafeName(GameClip clip)
    {
        string name = Path.GetFileNameWithoutExtension(clip.FileName);
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(name) ? "clip" : name;
    }

    public static bool CopyText(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("Share.CopyLink", ex);
            return false;
        }
    }

    /// <summary>The video itself on the clipboard, to paste into Discord or a chat.</summary>
    public static async Task<bool> CopyFileAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage
            {
                RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
            };
            package.SetStorageItems(new[] { file });
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("Share.CopyFile", ex);
            return false;
        }
    }
}
