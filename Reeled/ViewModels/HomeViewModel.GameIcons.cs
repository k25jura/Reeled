using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Reeled.Models;
using Reeled.Services;

namespace Reeled.ViewModels;

/// <summary>Game icons on folders: cached ones show immediately, new folders are looked up quietly.</summary>
public partial class HomeViewModel
{
    private static readonly TimeSpan RetryNotFoundAfter = TimeSpan.FromDays(14);
    private CancellationTokenSource? _iconLookupCts;
    private readonly Dictionary<string, BitmapImage> _iconImages = new();

    public event Action? GameIconsChanged;

    private IEnumerable<DirectoryNode> AllNodes()
    {
        var stack = new Stack<DirectoryNode>(Directories);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var sub in node.SubDirectories) stack.Push(sub);
        }
    }

    /// <summary>The game icon of the folder clips with this folder name sit in, for folder tags.</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? FolderIcon(string folderName) =>
        AllNodes().FirstOrDefault(n => n.GameIcon != null &&
            string.Equals(n.DisplayName, folderName, StringComparison.CurrentCultureIgnoreCase))?.GameIcon;

    private BitmapImage? LoadIconImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        if (_iconImages.TryGetValue(path, out var cached)) return cached;
        var image = new BitmapImage { DecodePixelWidth = 40, UriSource = new Uri(path) };
        _iconImages[path] = image;
        return image;
    }

    /// <summary>Puts known icons on folders and starts looking up the rest.</summary>
    public void RefreshGameIcons()
    {
        var settings = _storageService.CurrentSettings;
        GameIconCredentials.Load(settings);
        var pending = new List<DirectoryNode>();
        foreach (var node in AllNodes())
        {
            if (node.IsWatchRoot && node.SubDirectories.Count > 0)
            {
                // A watch root holding game folders is a library, not a game
                node.GameIcon = null;
                continue;
            }
            string key = GameIconService.KeyFor(node.DisplayName);
            if (settings.FolderGameIcons.TryGetValue(key, out var info))
            {
                node.GameIcon = info.Source is GameIconSource.Steam or GameIconSource.SteamManual or GameIconSource.Custom
                    ? LoadIconImage(info.ImagePath)
                    : null;
                bool retry = info.Source == GameIconSource.None && DateTime.UtcNow - info.CheckedUtc > RetryNotFoundAfter;
                if (!retry && (node.GameIcon != null || info.Source != GameIconSource.Steam)) continue;
            }
            else
            {
                node.GameIcon = null;
            }
            if (settings.LookUpGameIcons && GameIconService.LooksLikeGame(node.DisplayName) && node.ClipCount > 0)
            {
                pending.Add(node);
            }
        }
        // Views that build folder chips in code re-read the icons
        GameIconsChanged?.Invoke();
        if (pending.Count > 0) _ = LookUpIconsAsync(pending);
    }

    /// <summary>Stops automatic lookups in progress; icons already found stay.</summary>
    public void StopGameIconLookups()
    {
        _iconLookupCts?.Cancel();
        _iconLookupCts = null;
    }

    private async Task LookUpIconsAsync(List<DirectoryNode> nodes)
    {
        _iconLookupCts?.Cancel();
        var cts = new CancellationTokenSource();
        _iconLookupCts = cts;
        var settings = _storageService.CurrentSettings;
        var done = new HashSet<string>();

        foreach (var node in nodes)
        {
            if (cts.IsCancellationRequested) return;
            string key = GameIconService.KeyFor(node.DisplayName);
            if (!done.Add(key)) continue;
            try
            {
                var info = await Task.Run(() => GameIconService.LookUpAsync(node.DisplayName, cts.Token), cts.Token);
                // The user may have picked something by hand meanwhile
                if (settings.FolderGameIcons.TryGetValue(key, out var current) &&
                    current.Source is GameIconSource.SteamManual or GameIconSource.Custom or GameIconSource.Disabled) continue;
                settings.FolderGameIcons[key] = info;
                _dispatcherQueue.TryEnqueue(() => ApplyIconToNodes(key, info));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Offline or the providers said no; try again another day
                Helpers.Log.Error("GameIcon lookup", $"{node.DisplayName}: {ex.Message}");
                settings.FolderGameIcons[key] = new GameIconInfo { Source = GameIconSource.None, CheckedUtc = DateTime.UtcNow - RetryNotFoundAfter + TimeSpan.FromDays(1) };
            }
        }
        _ = _storageService.SaveSettingsAsync(settings);
    }

    private void ApplyIconToNodes(string key, GameIconInfo info)
    {
        var image = info.Source is GameIconSource.Steam or GameIconSource.SteamManual or GameIconSource.Custom
            ? LoadIconImage(info.ImagePath)
            : null;
        foreach (var node in AllNodes())
        {
            if (node.IsWatchRoot && node.SubDirectories.Count > 0) continue;
            if (GameIconService.KeyFor(node.DisplayName) == key) node.GameIcon = image;
        }
        GameIconsChanged?.Invoke();
    }

    /// <summary>Applies a game picked from the search results of any provider; throws when its icon can't be downloaded.</summary>
    public async Task SetIconAsync(DirectoryNode node, GameIconResult game)
    {
        string key = GameIconService.KeyFor(node.DisplayName);
        var path = await Task.Run(() => GameIconService.DownloadAsync(game));
        var info = new GameIconInfo { Source = GameIconSource.SteamManual, Provider = game.Provider, AppId = game.Id, GameName = game.Name, ImagePath = path, CheckedUtc = DateTime.UtcNow };
        _storageService.CurrentSettings.FolderGameIcons[key] = info;
        _ = _storageService.SaveSettingsAsync(_storageService.CurrentSettings);
        ApplyIconToNodes(key, info);
    }

    /// <summary>
    /// The icon sources (keys) changed: folders that were looked up without luck get another go, now
    /// that there may be more places to look.
    /// </summary>
    public void OnIconSourcesChanged()
    {
        var settings = _storageService.CurrentSettings;
        GameIconCredentials.Load(settings);
        var notFound = settings.FolderGameIcons.Where(kv => kv.Value.Source == GameIconSource.None).Select(kv => kv.Key).ToList();
        foreach (var key in notFound) settings.FolderGameIcons.Remove(key);
        if (notFound.Count > 0) _ = _storageService.SaveSettingsAsync(settings);
        if (settings.LookUpGameIcons) RefreshGameIcons();
    }

    public void SetCustomIcon(DirectoryNode node, string imagePath)
    {
        string key = GameIconService.KeyFor(node.DisplayName);
        var info = new GameIconInfo { Source = GameIconSource.Custom, ImagePath = imagePath, CheckedUtc = DateTime.UtcNow };
        _storageService.CurrentSettings.FolderGameIcons[key] = info;
        _ = _storageService.SaveSettingsAsync(_storageService.CurrentSettings);
        ApplyIconToNodes(key, info);
    }

    public void RemoveIcon(DirectoryNode node)
    {
        string key = GameIconService.KeyFor(node.DisplayName);
        var info = new GameIconInfo { Source = GameIconSource.Disabled, CheckedUtc = DateTime.UtcNow };
        _storageService.CurrentSettings.FolderGameIcons[key] = info;
        _ = _storageService.SaveSettingsAsync(_storageService.CurrentSettings);
        ApplyIconToNodes(key, info);
    }
}
