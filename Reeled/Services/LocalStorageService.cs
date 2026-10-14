using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Reeled.Helpers;
using Reeled.Models;

namespace Reeled.Services;

public class LocalStorageService : ILocalStorageService
{
    private readonly string _settingsFilePath;
    private readonly string _backupFilePath;
    private AppSettings _currentSettings = new();

    // Writes are coalesced: rapid saves (volume wheel, slider drags) only hit the disk with the latest
    // snapshot. Sequence numbers make sure an older snapshot can never overwrite a newer one.
    private readonly object _pendingLock = new();
    private readonly object _fileLock = new();
    private string? _pendingJson;
    private long _pendingSeq;
    private long _nextSeq;
    private long _lastWrittenSeq;
    private bool _writerRunning;
    private Task _writerTask = Task.CompletedTask;

    public AppSettings CurrentSettings => _currentSettings;

    public LocalStorageService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(localAppData, "Reeled");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "settings.json");
        _backupFilePath = _settingsFilePath + ".bak";

        bool mainExists = File.Exists(_settingsFilePath);
        var loaded = TryLoad(_settingsFilePath);
        bool needsWrite = false;

        if (loaded == null && mainExists)
        {
            // Never overwrite an unreadable settings file with defaults: keep it for recovery
            try
            {
                string corruptPath = Path.Combine(appFolder, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Move(_settingsFilePath, corruptPath, overwrite: true);
                Log.Error("Settings", $"settings.json could not be read; moved to {corruptPath}");
            }
            catch (Exception ex)
            {
                Log.Error("Settings", ex);
            }

            loaded = TryLoad(_backupFilePath);
            needsWrite = loaded != null;
        }

        if (loaded != null)
        {
            loaded.HasInitializedDefaults = true;
            _currentSettings = loaded;
            EnsureValidDefaults(_currentSettings);
        }
        else
        {
            _currentSettings = new AppSettings();
            EnsureValidDefaults(_currentSettings);
            needsWrite = true;
        }

        if (needsWrite)
        {
            SaveSettingsSync(_currentSettings);
        }
    }

    private static AppSettings? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppSettings);
        }
        catch (Exception ex)
        {
            Log.Error($"Settings.Load {Path.GetFileName(path)}", ex);
            return null;
        }
    }

    /// <summary>
    /// The in-memory settings are the source of truth; re-reading the file could resurrect stale
    /// values while a write is still in flight.
    /// </summary>
    public Task<AppSettings> LoadSettingsAsync() => Task.FromResult(_currentSettings);

    public Task SaveSettingsAsync(AppSettings settings)
    {
        _currentSettings = settings;

        string json;
        try
        {
            // Serialize on the caller's thread: settings are mutated from the UI thread
            json = JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.AppSettings);
        }
        catch (Exception ex)
        {
            Log.Error("Settings.Serialize", ex);
            return Task.CompletedTask;
        }

        lock (_pendingLock)
        {
            _pendingJson = json;
            _pendingSeq = ++_nextSeq;
            if (!_writerRunning)
            {
                _writerRunning = true;
                _writerTask = Task.Run(WriterLoop);
            }
            return _writerTask;
        }
    }

    private void WriterLoop()
    {
        while (true)
        {
            string? json;
            long seq;
            lock (_pendingLock)
            {
                json = _pendingJson;
                seq = _pendingSeq;
                _pendingJson = null;
                if (json == null)
                {
                    _writerRunning = false;
                    return;
                }
            }

            WriteIfNewer(json, seq);
        }
    }

    public void SaveSettingsSync(AppSettings settings)
    {
        _currentSettings = settings;
        try
        {
            string json = JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.AppSettings);
            long seq;
            lock (_pendingLock)
            {
                seq = ++_nextSeq;
                _pendingJson = null;
            }
            WriteIfNewer(json, seq);
        }
        catch (Exception ex)
        {
            Log.Error("Settings.SaveSync", ex);
        }
    }

    private void WriteIfNewer(string json, long seq)
    {
        lock (_fileLock)
        {
            if (seq <= _lastWrittenSeq) return;
            try
            {
                WriteAtomic(json);
                _lastWrittenSeq = seq;
            }
            catch (Exception ex)
            {
                Log.Error("Settings.Write", ex);
            }
        }
    }

    private void WriteAtomic(string json)
    {
        string tempPath = _settingsFilePath + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(_settingsFilePath))
        {
            try
            {
                File.Replace(tempPath, _settingsFilePath, _backupFilePath, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException)
            {
                // File.Replace is unsupported on some volumes; fall back to copy + move
                File.Copy(_settingsFilePath, _backupFilePath, overwrite: true);
            }
        }
        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }

    private static void EnsureValidDefaults(AppSettings settings)
    {
        settings.WatchDirectories ??= new();
        settings.Favorites ??= new();
        settings.Bookmarks ??= new();
        settings.CustomClipTitles ??= new();
        settings.ResumePositions ??= new();

        // Only initialize default directories on first run
        if (!settings.HasInitializedDefaults)
        {
            settings.HasInitializedDefaults = true;
            if (settings.WatchDirectories.Count == 0)
            {
                string myVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (Directory.Exists(myVideos) && !settings.WatchDirectories.Contains(myVideos))
                {
                    // The Captures subfolder is covered by the recursive scan of Videos
                    settings.WatchDirectories.Add(myVideos);
                }
            }
        }

        if (settings.PlaybackSpeed < 0.25)
        {
            settings.PlaybackSpeed = 1.0;
        }
        if (settings.DefaultPlaybackSpeed < 0.25)
        {
            settings.DefaultPlaybackSpeed = 1.0;
        }
        if (settings.DefaultVolume <= 0)
        {
            settings.DefaultVolume = settings.Volume > 0 ? settings.Volume : 100;
        }
        settings.DefaultVolume = Math.Clamp(settings.DefaultVolume, 0, 100);
        settings.Volume = Math.Clamp(settings.Volume, 0, 100);
    }
}
