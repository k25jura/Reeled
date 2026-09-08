using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage.Pickers;
using Reeled.Services;

namespace Reeled.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILocalStorageService _storageService;
    private readonly IClipIndexerService _indexerService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<string> WatchFolders { get; } = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _cacheSizeFormatted = "Calculating...";

    public SettingsViewModel(
        ILocalStorageService storageService,
        IClipIndexerService indexerService,
        INavigationService navigationService)
    {
        _storageService = storageService;
        _indexerService = indexerService;
        _navigationService = navigationService;
    }

    public void Initialize()
    {
        WatchFolders.Clear();
        foreach (var folder in _storageService.CurrentSettings.WatchDirectories)
        {
            WatchFolders.Add(folder);
        }
        CalculateCacheSize();
    }

    [RelayCommand]
    public async Task AddFolderAsync(IntPtr hwnd)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null && !WatchFolders.Contains(folder.Path))
            {
                WatchFolders.Add(folder.Path);
                var settings = _storageService.CurrentSettings;
                settings.WatchDirectories.Add(folder.Path);
                await _storageService.SaveSettingsAsync(settings);
                _indexerService.UpdateWatchers(settings.WatchDirectories);
                StatusMessage = $"Added {folder.Path}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RemoveFolderAsync(string folder)
    {
        if (WatchFolders.Remove(folder))
        {
            var settings = _storageService.CurrentSettings;
            settings.WatchDirectories.Remove(folder);
            await _storageService.SaveSettingsAsync(settings);
            _indexerService.UpdateWatchers(settings.WatchDirectories);
            StatusMessage = $"Removed {folder}";
        }
    }

    [RelayCommand]
    public void ClearCache()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cacheDir = Path.Combine(localAppData, "Reeled", "Thumbnails");
            if (Directory.Exists(cacheDir))
            {
                foreach (var file in Directory.EnumerateFiles(cacheDir))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            CalculateCacheSize();
            StatusMessage = "Thumbnail cache cleared";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void BackToHome()
    {
        _navigationService.NavigateToHome();
    }

    private void CalculateCacheSize()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cacheDir = Path.Combine(localAppData, "Reeled", "Thumbnails");
            if (!Directory.Exists(cacheDir))
            {
                CacheSizeFormatted = "0 KB";
                return;
            }

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(cacheDir))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }

            double mb = total / (1024.0 * 1024.0);
            CacheSizeFormatted = $"{mb:F1} MB";
        }
        catch
        {
            CacheSizeFormatted = "0 KB";
        }
    }
}
