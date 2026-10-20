using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Helpers;
using Reeled.Models;
using Reeled.Services;

namespace Reeled.ViewModels;

/// <summary>
/// File-explorer style management of the library: moving, copying and deleting clips, and creating,
/// renaming and deleting folders. The file system stays the source of truth; the watcher reports changes made
/// elsewhere, and what the app does itself is applied directly (the watcher's later echo is a no-op).
/// Everything per-clip that is kept in settings (favorites, tags, moments, titles, resume positions) is keyed by
/// file path, so it is re-keyed whenever a path changes.
/// </summary>
public partial class HomeViewModel
{
    private bool _quietSync;

    private IFileOperationService FileOps => App.GetService<IFileOperationService>();

    /// <summary>The folder that shows now, or null on Home, Favorites and Saved Moments.</summary>
    public string? CurrentFolderPath =>
        CurrentSection == NavigationSection.Folder ? SelectedDirectory?.FullPath : null;

    // ---------------------------------------------------------------------------------------------
    // Clips
    // ---------------------------------------------------------------------------------------------

    /// <summary>Copies or moves files into a library folder. The callbacks run on the calling (UI) thread.</summary>
    public Task<FileTransferResult> TransferClipsAsync(
        IReadOnlyList<string> sources,
        string destinationFolder,
        FileTransferKind kind,
        Func<FileConflict, Task<ConflictDecision>> resolveConflict,
        IProgress<FileTransferProgress>? progress,
        CancellationToken cancellationToken) =>
        FileOps.TransferAsync(sources, destinationFolder, kind, resolveConflict, progress, done => ApplyTransferred(done, kind), cancellationToken);

    private void ApplyTransferred(TransferredFile file, FileTransferKind kind)
    {
        if (kind == FileTransferKind.Copy)
        {
            AddClipFromDisk(file.Destination);
            return;
        }

        MigrateClipState(file.Source, file.Destination);
        var moving = FindClip(file.Source);
        var atDestination = FindClip(file.Destination);
        if (moving != null)
        {
            // A replaced file was already sent to the Recycle Bin: its entry makes way for the moved clip
            if (atDestination != null && !ReferenceEquals(atDestination, moving)) RemoveClipFromLibrary(file.Destination);
            ApplyRename(file.Source, file.Destination);
        }
        else if (atDestination == null)
        {
            AddClipFromDisk(file.Destination);
        }
    }

    private void AddClipFromDisk(string path)
    {
        // A replaced file's old entry is dropped so the new file's size and length are read afresh
        var existing = FindClip(path);
        if (existing != null) RemoveClipFromLibrary(path);

        if (_syncTask != null) _ = SyncDirectoriesAsync(forceReload: true);
        else AddClipCore(path);
    }

    /// <summary>Sends clips to the Recycle Bin in one go. Returns how many are still on disk (could not be moved).</summary>
    public async Task<int> DeleteClipsAsync(IReadOnlyList<GameClip> clips)
    {
        var paths = clips.Select(c => c.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return 0;

        await FileOps.MoveToRecycleBinAsync(paths, App.WindowHandle);

        var gone = paths.Where(p => !File.Exists(p)).ToList();
        RemoveClipsFromLibrary(gone);
        ForgetClipStates(gone);
        int failed = paths.Count - gone.Count;
        StatusMessage = failed == 0
            ? (gone.Count == 1 ? $"Deleted {Path.GetFileName(gone[0])} to Recycle Bin" : $"Deleted {gone.Count} clips to Recycle Bin")
            : $"Failed to delete {failed} clips";
        return failed;
    }

    private void RemoveClipsFromLibrary(IEnumerable<string> paths)
    {
        bool any = false;
        foreach (var path in paths)
        {
            var clip = FindClip(path);
            if (clip == null) continue;
            AllClips.Remove(clip);
            Clips.Remove(clip);
            AdjustFolderCounts(path, -1);
            any = true;
        }
        if (!any) return;
        ApplyFilterAndSort();
        UpdateCounts();
    }

    private void ForgetClipStates(IEnumerable<string> paths)
    {
        var settings = _storageService.CurrentSettings;
        bool changed = false;
        foreach (var path in paths)
        {
            changed |= settings.Favorites.Remove(path);
            changed |= settings.Bookmarks.Remove(path);
            changed |= settings.CustomClipTitles.Remove(path);
            changed |= settings.ResumePositions.Remove(path);
            changed |= settings.ClipTags.Remove(path);
        }
        if (changed) _ = _storageService.SaveSettingsAsync(settings);
    }

    // ---------------------------------------------------------------------------------------------
    // Folders
    // ---------------------------------------------------------------------------------------------

    /// <summary>Creates a folder and shows it in the tree right away (no rescan).</summary>
    public DirectoryNode CreateFolder(string parentPath, string name)
    {
        _indexerService.SuppressLibraryChanged(TimeSpan.FromSeconds(3));
        string path = FileOps.CreateFolder(parentPath, name);

        var node = new DirectoryNode { FullPath = path, Name = Path.GetFileName(path), ClipCount = 0 };
        var parent = FindNodeByPath(Directories, parentPath);
        if (parent != null)
        {
            int index = 0;
            while (index < parent.SubDirectories.Count &&
                   string.Compare(parent.SubDirectories[index].Name, node.Name, StringComparison.OrdinalIgnoreCase) < 0)
            {
                index++;
            }
            parent.SubDirectories.Insert(index, node);
            parent.HasSubDirectories = true;
        }
        StatusMessage = $"Created {node.Name}";
        return node;
    }

    /// <summary>Renames a folder on disk and takes everything that points into it along.</summary>
    public async Task RenameFolderAsync(DirectoryNode node, string newName)
    {
        string oldPath = TrimSeparators(node.FullPath);
        string oldName = node.DisplayName;
        var settings = _storageService.CurrentSettings;

        // The watchers keep the watch folders open, which would block renaming one of them
        _indexerService.UpdateWatchers(Array.Empty<string>());
        string newPath;
        try
        {
            newPath = await Task.Run(() => FileOps.RenameFolder(oldPath, newName));
        }
        catch
        {
            _indexerService.UpdateWatchers(settings.WatchDirectories);
            throw;
        }

        RetargetPaths(oldPath, newPath);
        MigrateFolderIcon(oldName, Path.GetFileName(newPath));
        await QuietResyncAsync();
        StatusMessage = $"Renamed to {Path.GetFileName(newPath)}";
    }

    /// <summary>Sends a folder (and everything in it) to the Recycle Bin. Returns false if it is still there.</summary>
    public async Task<bool> DeleteFolderAsync(DirectoryNode node)
    {
        string path = TrimSeparators(node.FullPath);
        var settings = _storageService.CurrentSettings;
        string? parent = Path.GetDirectoryName(path);
        if (parent == null || !settings.WatchDirectories.Any(root => PathUtil.IsUnder(parent, root))) parent = null;

        var clipPaths = AllClips.Where(c => PathUtil.IsUnder(c.FilePath, path)).Select(c => c.FilePath).ToList();

        // The watchers keep the watch folders open, which would block removing one of them
        _indexerService.UpdateWatchers(Array.Empty<string>());
        bool removed = await FileOps.MoveToRecycleBinAsync(new[] { path }, App.WindowHandle);

        if (!Directory.Exists(path)) ForgetClipStates(clipPaths);
        else ForgetClipStates(clipPaths.Where(p => !File.Exists(p)));

        if (!Directory.Exists(path))
        {
            settings.WatchDirectories.RemoveAll(w => PathUtil.IsUnder(w, path));
            if (PathUtil.IsUnder(settings.LastActiveDirectory, path)) settings.LastActiveDirectory = parent;
            foreach (var tab in _tabs.Where(t => t.Section == NavigationSection.Folder && PathUtil.IsUnder(t.FolderPath, path)))
            {
                if (parent != null)
                {
                    tab.FolderPath = parent;
                }
                else
                {
                    tab.Section = NavigationSection.Home;
                    tab.FolderPath = null;
                }
                TabStateChanged?.Invoke(tab);
            }
            PersistTabs();
            _ = _storageService.SaveSettingsAsync(settings);
        }

        await QuietResyncAsync();
        StatusMessage = removed ? $"Deleted {node.DisplayName} to Recycle Bin" : $"Failed to delete {node.DisplayName}";
        return !Directory.Exists(path);
    }

    private async Task QuietResyncAsync()
    {
        _quietSync = true;
        try
        {
            await SyncDirectoriesAsync(forceReload: true);
        }
        finally
        {
            _quietSync = false;
        }
    }

    private static string TrimSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string Retarget(string path, string oldRoot, string newRoot) =>
        newRoot + path.Substring(oldRoot.Length);

    /// <summary>
    /// A folder was renamed: watch folders, the remembered folder, open tabs and every per-clip setting
    /// that is keyed by file path now point at the new location.
    /// </summary>
    private void RetargetPaths(string oldRoot, string newRoot)
    {
        var settings = _storageService.CurrentSettings;

        for (int i = 0; i < settings.WatchDirectories.Count; i++)
        {
            if (PathUtil.IsUnder(settings.WatchDirectories[i], oldRoot))
                settings.WatchDirectories[i] = Retarget(settings.WatchDirectories[i], oldRoot, newRoot);
        }
        if (PathUtil.IsUnder(settings.LastActiveDirectory, oldRoot))
            settings.LastActiveDirectory = Retarget(settings.LastActiveDirectory!, oldRoot, newRoot);

        foreach (var tab in _tabs.Where(t => PathUtil.IsUnder(t.FolderPath, oldRoot)))
        {
            tab.FolderPath = Retarget(tab.FolderPath!, oldRoot, newRoot);
            TabStateChanged?.Invoke(tab);
        }
        PersistTabs();

        var favorites = settings.Favorites.Where(p => PathUtil.IsUnder(p, oldRoot)).ToList();
        foreach (var path in favorites)
        {
            settings.Favorites.Remove(path);
            settings.Favorites.Add(Retarget(path, oldRoot, newRoot));
        }
        RekeyUnder(settings.ClipTags, oldRoot, newRoot);
        RekeyUnder(settings.Bookmarks, oldRoot, newRoot);
        RekeyUnder(settings.CustomClipTitles, oldRoot, newRoot);
        RekeyUnder(settings.ResumePositions, oldRoot, newRoot);

        _ = _storageService.SaveSettingsAsync(settings);
    }

    private static void RekeyUnder<T>(Dictionary<string, T> map, string oldRoot, string newRoot)
    {
        foreach (var key in map.Keys.Where(k => PathUtil.IsUnder(k, oldRoot)).ToList())
        {
            var value = map[key];
            map.Remove(key);
            map[Retarget(key, oldRoot, newRoot)] = value;
        }
    }

    /// <summary>Game icons are remembered by folder name: one the user chose follows a rename.</summary>
    private void MigrateFolderIcon(string oldName, string newName)
    {
        var settings = _storageService.CurrentSettings;
        string oldKey = GameIconService.KeyFor(oldName);
        string newKey = GameIconService.KeyFor(newName);
        if (oldKey == newKey || settings.FolderGameIcons.ContainsKey(newKey)) return;
        if (settings.FolderGameIcons.TryGetValue(oldKey, out var info) &&
            info.Source is GameIconSource.SteamManual or GameIconSource.Custom or GameIconSource.Disabled)
        {
            settings.FolderGameIcons[newKey] = info;
        }
    }
}
