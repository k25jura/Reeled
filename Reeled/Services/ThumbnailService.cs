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

                // Extract via Windows Shell at optimized 360px width
                var storageFile = await StorageFile.GetFileFromPathAsync(path);
                using var thumb = await storageFile.GetThumbnailAsync(
                    ThumbnailMode.VideosView,
                    360,
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
            if (!File.Exists(filePath))
                return null;

            var dispatcher = App.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            if (dispatcher == null)
                return null;

            var tcs = new TaskCompletionSource<BitmapImage?>();
            bool enqueued = dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(filePath);
                    using var stream = await file.OpenReadAsync();
                    var bitmap = new BitmapImage();
                    bitmap.DecodePixelWidth = 360;
                    await bitmap.SetSourceAsync(stream);
                    tcs.TrySetResult(bitmap);
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText("reeled_crash.log", $"[LoadBitmap Error] {filePath}: {ex.Message}\n"); } catch { }
                    tcs.TrySetResult(null);
                }
            });

            if (!enqueued)
                return null;

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("reeled_crash.log", $"[LoadBitmap Outer Error] {filePath}: {ex.Message}\n"); } catch { }
            return null;
        }
    }

    public void ClearMemoryCache()
    {
        _inFlightThumbnails.Clear();
    }
}
