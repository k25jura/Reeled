using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Reeled.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, Task<BitmapImage?>> _inFlightThumbnails = new();

    public ThumbnailService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDirectory = Path.Combine(localAppData, "Reeled", "Thumbnails");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public Task<BitmapImage?> GetThumbnailAsync(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            return Task.FromResult<BitmapImage?>(null);

        return _inFlightThumbnails.GetOrAdd(videoPath, async path =>
        {
            try
            {
                string cacheKey = ComputeCacheKey(path);
                string cacheFile = Path.Combine(_cacheDirectory, $"{cacheKey}.jpg");

                if (File.Exists(cacheFile))
                {
                    return await LoadBitmapFromDiskAsync(cacheFile);
                }

                // Extract via Windows Shell
                var storageFile = await StorageFile.GetFileFromPathAsync(path);
                using var thumb = await storageFile.GetThumbnailAsync(
                    ThumbnailMode.VideosView,
                    480,
                    ThumbnailOptions.ResizeThumbnail);

                if (thumb != null && thumb.Size > 0)
                {
                    // Save to disk cache
                    using (var inputStream = thumb.AsStreamForRead())
                    using (var fileStream = File.Create(cacheFile))
                    {
                        await inputStream.CopyToAsync(fileStream);
                    }

                    return await LoadBitmapFromDiskAsync(cacheFile);
                }
            }
            catch (Exception)
            {
                // Fallback gracefully on shell thumbnail failure
            }
            finally
            {
                _inFlightThumbnails.TryRemove(videoPath, out _);
            }

            return null;
        });
    }

    private static string ComputeCacheKey(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            string raw = $"{path}_{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}";
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
        }
        catch
        {
            return Math.Abs(path.GetHashCode()).ToString();
        }
    }

    private static async Task<BitmapImage?> LoadBitmapFromDiskAsync(string filePath)
    {
        try
        {
            var tcs = new TaskCompletionSource<BitmapImage?>();
            var dispatcher = DispatcherQueue.GetForCurrentThread();

            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        var bitmap = new BitmapImage(new Uri(filePath));
                        tcs.SetResult(bitmap);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                return await tcs.Task;
            }
            else
            {
                var bitmap = new BitmapImage(new Uri(filePath));
                return bitmap;
            }
        }
        catch
        {
            return null;
        }
    }
}
