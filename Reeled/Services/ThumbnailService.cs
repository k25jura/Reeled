using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Reeled.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapImage?>>> _inFlightThumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _diskExtractionThrottler = new(4, 4);
    // Requests made for something on screen get their own lane so they never wait behind the
    // library-wide background warm-up that queues an extraction for every clip.
    private readonly SemaphoreSlim _interactiveThrottler = new(3, 3);

    // Bounded LRU in-memory cache for BitmapImages (~290 KB each at 360px decode width, ~45 MB max).
    // Grid cards release their bitmap when recycled, so this is the only long-lived owner.
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _lruList = new();
    private const int MaxMemoryCacheEntries = 160;

    private record CacheEntry(string Key, BitmapImage Bitmap);

    public ThumbnailService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDirectory = Path.Combine(localAppData, "Reeled", "Thumbnails");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public bool TryGetFromMemoryCache(string videoPath, out BitmapImage? bitmap)
    {
        if (string.IsNullOrEmpty(videoPath))
        {
            bitmap = null;
            return false;
        }

        lock (_cacheLock)
        {
            if (_memoryCache.TryGetValue(videoPath, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }

            bitmap = null;
            return false;
        }
    }

    private void AddToMemoryCache(string videoPath, BitmapImage bitmap)
    {
        if (string.IsNullOrEmpty(videoPath) || bitmap == null)
            return;

        lock (_cacheLock)
        {
            if (_memoryCache.TryGetValue(videoPath, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _lruList.AddFirst(existingNode);
                return;
            }

            if (_memoryCache.Count >= MaxMemoryCacheEntries)
            {
                var oldest = _lruList.Last;
                if (oldest != null)
                {
                    _lruList.RemoveLast();
                    _memoryCache.Remove(oldest.Value.Key);
                }
            }

            var newNode = new LinkedListNode<CacheEntry>(new CacheEntry(videoPath, bitmap));
            _lruList.AddFirst(newNode);
            _memoryCache[videoPath] = newNode;
        }
    }

    public async Task<string?> EnsureThumbnailOnDiskAsync(string videoPath, bool highPriority = false)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            return null;

        try
        {
            string cacheKey = ComputeCacheKey(videoPath);
            string cacheFile = Path.Combine(_cacheDirectory, $"{cacheKey}.jpg");

            if (File.Exists(cacheFile))
                return cacheFile;

            var throttler = highPriority ? _interactiveThrottler : _diskExtractionThrottler;
            await throttler.WaitAsync();
            try
            {
                if (File.Exists(cacheFile))
                    return cacheFile;

                var storageFile = await StorageFile.GetFileFromPathAsync(videoPath);
                using var thumb = await storageFile.GetThumbnailAsync(
                    ThumbnailMode.VideosView,
                    360,
                    ThumbnailOptions.ResizeThumbnail);

                if (thumb != null && thumb.Size > 0)
                {
                    string tempFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var inputStream = thumb.AsStreamForRead())
                    using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                    {
                        await inputStream.CopyToAsync(fileStream);
                    }

                    try
                    {
                        File.Move(tempFile, cacheFile, overwrite: true);
                    }
                    catch
                    {
                        try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                    }

                    return cacheFile;
                }
            }
            finally
            {
                throttler.Release();
            }
        }
        catch (Exception)
        {
            // Fallback gracefully on shell thumbnail failure
        }

        return null;
    }

    public Task<BitmapImage?> GetThumbnailAsync(string videoPath, bool highPriority = false)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            return Task.FromResult<BitmapImage?>(null);

        if (TryGetFromMemoryCache(videoPath, out var cachedBitmap))
        {
            return Task.FromResult<BitmapImage?>(cachedBitmap);
        }

        // Lazy guarantees a single load per path. The entry is removed only after the load has been
        // published, so a synchronously completing load can't leave a stale entry behind.
        var lazy = _inFlightThumbnails.GetOrAdd(videoPath, path => new Lazy<Task<BitmapImage?>>(() => LoadThumbnailAsync(path, highPriority)));
        var task = lazy.Value;
        _ = task.ContinueWith(_ => _inFlightThumbnails.TryRemove(new KeyValuePair<string, Lazy<Task<BitmapImage?>>>(videoPath, lazy)),
            TaskScheduler.Default);
        return task;
    }

    private async Task<BitmapImage?> LoadThumbnailAsync(string path, bool highPriority)
    {
        try
        {
            if (TryGetFromMemoryCache(path, out var doubleCheckBitmap))
            {
                return doubleCheckBitmap;
            }

            string? cacheFile = await EnsureThumbnailOnDiskAsync(path, highPriority);
            if (cacheFile != null && File.Exists(cacheFile))
            {
                var bmp = await LoadBitmapFromDiskAsync(cacheFile);
                if (bmp != null)
                {
                    AddToMemoryCache(path, bmp);
                }
                return bmp;
            }
        }
        catch (Exception)
        {
            // Fallback gracefully on shell thumbnail failure
        }

        return null;
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

            // Open with FileShare.ReadWrite | FileShare.Delete so background writes or indexers never lock it out
            byte[] bytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))
            {
                if (fs.Length == 0) return null;
                bytes = new byte[fs.Length];
                await fs.ReadExactlyAsync(bytes, 0, bytes.Length);
            }

            if (bytes == null || bytes.Length == 0)
                return null;

            var tcs = new TaskCompletionSource<BitmapImage?>();
            bool enqueued = dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var memStream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(memStream))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }
                    memStream.Seek(0);

                    var bitmap = new BitmapImage();
                    bitmap.DecodePixelWidth = 360;
                    await bitmap.SetSourceAsync(memStream);
                    tcs.TrySetResult(bitmap);
                }
                catch (Exception ex)
                {
                    Helpers.Log.Error("LoadBitmap Error", $"{filePath}: {ex.Message}");
                    tcs.TrySetResult(null);
                }
            });

            if (!enqueued)
                return null;

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("LoadBitmap Outer Error", $"{filePath}: {ex.Message}");
            return null;
        }
    }

    public void ClearMemoryCache()
    {
        lock (_cacheLock)
        {
            _memoryCache.Clear();
            _lruList.Clear();
        }
        _inFlightThumbnails.Clear();
    }
}
