using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.Storage;
using Reeled.Models;

namespace Reeled.Services;

public class ClipMetadataService : IClipMetadataService
{
    private readonly IThumbnailService _thumbnailService;
    private readonly IClipMetadataCacheService _cacheService;

    public ClipMetadataService(IThumbnailService thumbnailService, IClipMetadataCacheService cacheService)
    {
        _thumbnailService = thumbnailService;
        _cacheService = cacheService;
    }

    public async Task PopulateMetadataAsync(GameClip clip)
    {
        string path = clip.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        // Probe everything off the UI thread first. GameClip properties are x:Bind sources, so they
        // must only be assigned on the UI thread: a cross-thread PropertyChanged throws inside the setter.
        FileInfo fileInfo;
        TimeSpan? duration = null;
        uint width = 0, height = 0;
        try
        {
            fileInfo = new FileInfo(path);
            if (_cacheService.TryGet(path, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, out var cached) && cached != null)
            {
                duration = TimeSpan.FromTicks(cached.DurationTicks);
                width = cached.VideoWidth;
                height = cached.VideoHeight;
            }
            else
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(path);
                var videoProps = await storageFile.Properties.GetVideoPropertiesAsync();
                if (videoProps != null)
                {
                    duration = videoProps.Duration;
                    width = videoProps.Width;
                    height = videoProps.Height;

                    _cacheService.Set(new CachedClipMetadata
                    {
                        FilePath = path,
                        FileSizeBytes = fileInfo.Length,
                        LastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                        DurationTicks = videoProps.Duration.Ticks,
                        VideoWidth = videoProps.Width,
                        VideoHeight = videoProps.Height,
                        CreatedDateUtc = fileInfo.CreationTimeUtc,
                        ModifiedDateUtc = fileInfo.LastWriteTimeUtc
                    });
                }
            }
        }
        catch (Exception)
        {
            // WinRT property handler failures are expected for some containers; keep basic file info
            return;
        }

        RunOnUiThread(() =>
        {
            if (!string.Equals(clip.FilePath, path, StringComparison.OrdinalIgnoreCase)) return;

            if (clip.FileSizeBytes != fileInfo.Length) clip.FileSizeBytes = fileInfo.Length;
            if (clip.CreatedDate != fileInfo.CreationTimeUtc) clip.CreatedDate = fileInfo.CreationTimeUtc;
            if (clip.ModifiedDate != fileInfo.LastWriteTimeUtc) clip.ModifiedDate = fileInfo.LastWriteTimeUtc;
            if (duration.HasValue && duration.Value > TimeSpan.Zero) clip.Duration = duration.Value;
            if (width > 0) clip.VideoWidth = width;
            if (height > 0) clip.VideoHeight = height;
        });

        // Warm the on-disk thumbnail cache without creating a BitmapImage in memory
        _ = Task.Run(async () =>
        {
            try
            {
                string? thumbPath = await _thumbnailService.EnsureThumbnailOnDiskAsync(path);
                if (thumbPath != null)
                {
                    RunOnUiThread(() => clip.ThumbnailCachePath = thumbPath);
                }
            }
            catch { }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = App.DispatcherQueue;
        if (dispatcher == null || dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => action());
        }
    }
}
