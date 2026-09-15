using System;
using System.IO;
using System.Threading.Tasks;
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
        if (string.IsNullOrEmpty(clip.FilePath) || !File.Exists(clip.FilePath))
            return;

        try
        {
            var fileInfo = new FileInfo(clip.FilePath);
            clip.FileSizeBytes = fileInfo.Length;
            clip.CreatedDate = fileInfo.CreationTimeUtc;
            clip.ModifiedDate = fileInfo.LastWriteTimeUtc;
            clip.FileName = fileInfo.Name;
            clip.DirectoryPath = fileInfo.DirectoryName ?? string.Empty;

            if (_cacheService.TryGet(clip.FilePath, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, out var cached) && cached != null)
            {
                clip.Duration = TimeSpan.FromTicks(cached.DurationTicks);
                clip.VideoWidth = cached.VideoWidth;
                clip.VideoHeight = cached.VideoHeight;
            }
            else
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(clip.FilePath);
                var videoProps = await storageFile.Properties.GetVideoPropertiesAsync();

                if (videoProps != null)
                {
                    clip.Duration = videoProps.Duration;
                    clip.VideoWidth = videoProps.Width;
                    clip.VideoHeight = videoProps.Height;

                    _cacheService.Set(new CachedClipMetadata
                    {
                        FilePath = clip.FilePath,
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
            // If WinRT storage properties fail, basic FileInfo is already set
        }

        // Asynchronously load thumbnail in background so UI populates without blocking
        _ = Task.Run(async () =>
        {
            try
            {
                var thumb = await _thumbnailService.GetThumbnailAsync(clip.FilePath);
                if (thumb != null)
                {
                    var dispatcher = App.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    dispatcher?.TryEnqueue(() => clip.Thumbnail = thumb);
                }
            }
            catch { }
        });
    }
}
