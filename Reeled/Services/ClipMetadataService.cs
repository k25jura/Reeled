using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Reeled.Models;

namespace Reeled.Services;

public class ClipMetadataService : IClipMetadataService
{
    private readonly IThumbnailService _thumbnailService;

    public ClipMetadataService(IThumbnailService thumbnailService)
    {
        _thumbnailService = thumbnailService;
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

            var storageFile = await StorageFile.GetFileFromPathAsync(clip.FilePath);
            var videoProps = await storageFile.Properties.GetVideoPropertiesAsync();

            if (videoProps != null)
            {
                clip.Duration = videoProps.Duration;
                clip.VideoWidth = videoProps.Width;
                clip.VideoHeight = videoProps.Height;
            }
        }
        catch (Exception)
        {
            // If WinRT storage properties fail, basic FileInfo is already set
        }

        try
        {
            clip.Thumbnail = await _thumbnailService.GetThumbnailAsync(clip.FilePath);
        }
        catch (Exception)
        {
        }
    }
}
