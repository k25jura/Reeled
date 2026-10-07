using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public class LocalStorageService : ILocalStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AppSettings _currentSettings = new();

    public AppSettings CurrentSettings => _currentSettings;

    public LocalStorageService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(localAppData, "Reeled");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "settings.json");

        if (File.Exists(_settingsFilePath))
        {
            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppSettings);
                if (loaded != null)
                {
                    loaded.HasInitializedDefaults = true;
                    _currentSettings = loaded;
                    EnsureValidDefaults(_currentSettings);
                    SaveSettingsSync(_currentSettings);
                    return;
                }
            }
            catch { }
        }

        // Default fallback
        _currentSettings = new AppSettings();
        EnsureValidDefaults(_currentSettings);
        SaveSettingsSync(_currentSettings);
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = await File.ReadAllTextAsync(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppSettings);
                if (loaded != null)
                {
                    loaded.HasInitializedDefaults = true;
                    _currentSettings = loaded;
                    EnsureValidDefaults(_currentSettings);
                    return _currentSettings;
                }
            }

            // Default fallback
            _currentSettings = new AppSettings();
            EnsureValidDefaults(_currentSettings);
            return _currentSettings;
        }
        catch (Exception)
        {
            _currentSettings = new AppSettings();
            EnsureValidDefaults(_currentSettings);
            return _currentSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _currentSettings = settings;
            string json = JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.AppSettings);
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }
        catch (Exception)
        {
            // Ignore write errors to prevent crashes
        }
        finally
        {
            _lock.Release();
        }
    }

    public void SaveSettingsSync(AppSettings settings)
    {
        try
        {
            _currentSettings = settings;
            string json = JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.AppSettings);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception)
        {
        }
    }

    private static void EnsureValidDefaults(AppSettings settings)
    {
        if (settings.WatchDirectories == null)
        {
            settings.WatchDirectories = new();
        }

        // Only initialize default directories on first run
        if (!settings.HasInitializedDefaults)
        {
            settings.HasInitializedDefaults = true;
            if (settings.WatchDirectories.Count == 0)
            {
                string myVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (Directory.Exists(myVideos) && !settings.WatchDirectories.Contains(myVideos))
                {
                    settings.WatchDirectories.Add(myVideos);
                }
                string captures = Path.Combine(myVideos, "Captures");
                if (Directory.Exists(captures) && !settings.WatchDirectories.Contains(captures))
                {
                    settings.WatchDirectories.Add(captures);
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
