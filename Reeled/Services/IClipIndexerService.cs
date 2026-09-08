using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public interface IClipIndexerService : IDisposable
{
    event Action<string>? ClipAdded;
    event Action<string>? ClipDeleted;
    event Action<string, string>? ClipRenamed;

    Task<List<GameClip>> ScanDirectoryClipsAsync(string directoryPath, bool recursive = false);
    Task<List<DirectoryNode>> BuildDirectoryTreesAsync(IEnumerable<string> watchRoots);
    void UpdateWatchers(IEnumerable<string> watchRoots);
    bool DeleteClipToRecycleBin(string filePath);
    bool RenameClip(string filePath, string newFileName, out string newPath);
}
