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
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = await File.ReadAllTextAsync(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    _currentSettings = loaded;
                    return _currentSettings;
                }
            }

            // Default fallback
            _currentSettings = new AppSettings();
            string myVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (Directory.Exists(myVideos) && !_currentSettings.WatchDirectories.Contains(myVideos))
            {
                _currentSettings.WatchDirectories.Add(myVideos);
            }
            string captures = Path.Combine(myVideos, "Captures");
            if (Directory.Exists(captures) && !_currentSettings.WatchDirectories.Contains(captures))
            {
                _currentSettings.WatchDirectories.Add(captures);
            }

            return _currentSettings;
        }
        catch (Exception)
        {
            _currentSettings = new AppSettings();
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
            string json = JsonSerializer.Serialize(settings, JsonOptions);
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
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception)
        {
        }
    }
}
