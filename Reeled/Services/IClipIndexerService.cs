using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public interface IClipIndexerService : IDisposable
{
    /// <summary>Raised once a newly created clip has finished being written and can be opened.</summary>
    event Action<string>? ClipAdded;
    event Action<string>? ClipDeleted;
    event Action<string, string>? ClipRenamed;

    /// <summary>Raised when the watched tree changed in a way that requires a full rescan (folder changes, watcher overflow).</summary>
    event Action? LibraryChanged;

    /// <summary>Raised after background metadata population for a scan has finished.</summary>
    event Action? MetadataBatchCompleted;

    Task<List<GameClip>> ScanDirectoryClipsAsync(string directoryPath, bool recursive = false);
    Task<List<DirectoryNode>> BuildDirectoryTreesAsync(IEnumerable<string> watchRoots);
    void UpdateWatchers(IEnumerable<string> watchRoots);

    /// <summary>
    /// Folder changes the app makes itself (a new folder) update the tree directly: the full rescan that
    /// the watcher would ask for in the next moments is skipped.
    /// </summary>
    void SuppressLibraryChanged(TimeSpan duration);
    bool DeleteClipToRecycleBin(string filePath);
    bool RenameClip(string filePath, string newFileName, out string newPath);
}
