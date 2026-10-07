using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public class ClipMetadataCacheService : IClipMetadataCacheService
{
    private readonly ILocalStorageService _storageService;
    private readonly string _cacheFilePath;
    private readonly ConcurrentDictionary<string, CachedClipMetadata> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _isDirty;

    public ClipMetadataCacheService(ILocalStorageService storageService)
    {
        _storageService = storageService;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDir = Path.Combine(localAppData, "Reeled", "Cache");
        Directory.CreateDirectory(cacheDir);
        _cacheFilePath = Path.Combine(cacheDir, "clips_metadata_cache.json");
        LoadCache();
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                string json = File.ReadAllText(_cacheFilePath);
                var items = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListCachedClipMetadata);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrEmpty(item.FilePath))
                        {
                            _cache[item.FilePath] = item;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("reeled_crash.log", $"[ClipMetadataCache.Load Error] {ex}\n"); } catch { }
        }
    }

    public bool TryGet(string filePath, long length, long lastWriteTimeTicks, out CachedClipMetadata? cached)
    {
        if (!_storageService.CurrentSettings.EnableClipCache)
        {
            cached = null;
            return false;
        }

        if (_cache.TryGetValue(filePath, out var item))
        {
            if (item.FileSizeBytes == length && item.LastWriteTimeUtcTicks == lastWriteTimeTicks)
            {
                cached = item;
                return true;
            }
        }

        cached = null;
        return false;
    }

    public void Set(CachedClipMetadata metadata)
    {
        if (!_storageService.CurrentSettings.EnableClipCache) return;
        if (string.IsNullOrEmpty(metadata.FilePath)) return;
        _cache[metadata.FilePath] = metadata;
        _isDirty = true;
    }

    public async Task SaveAsync()
    {
        if (!_storageService.CurrentSettings.EnableClipCache) return;
        if (!_isDirty) return;

        await _saveLock.WaitAsync();
        try
        {
            if (!_isDirty) return;

            var list = new List<CachedClipMetadata>(_cache.Values);
            string json = JsonSerializer.Serialize(list, AppJsonSerializerContext.Default.ListCachedClipMetadata);
            string tempFile = _cacheFilePath + ".tmp";
            await File.WriteAllTextAsync(tempFile, json);
            File.Move(tempFile, _cacheFilePath, overwrite: true);
            _isDirty = false;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("reeled_crash.log", $"[ClipMetadataCache.Save Error] {ex}\n"); } catch { }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            _cache.Clear();
            _isDirty = false;
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("reeled_crash.log", $"[ClipMetadataCache.Clear Error] {ex}\n"); } catch { }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public (int count, long bytes) GetCacheStats()
    {
        int count = _cache.Count;
        long bytes = 0;
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                bytes = new FileInfo(_cacheFilePath).Length;
            }
        }
        catch { }

        return (count, bytes);
    }
}
