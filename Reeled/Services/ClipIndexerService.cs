using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Reeled.Models;

namespace Reeled.Services;

public class ClipIndexerService : IClipIndexerService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".wmv"
    };

    private readonly IClipMetadataService _metadataService;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly object _lock = new();

    public event Action<string>? ClipAdded;
    public event Action<string>? ClipDeleted;
    public event Action<string, string>? ClipRenamed;

    public ClipIndexerService(IClipMetadataService metadataService)
    {
        _metadataService = metadataService;
    }

    public async Task<List<GameClip>> ScanDirectoryClipsAsync(string directoryPath, bool recursive = false)
    {
        var clips = new List<GameClip>();
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            return clips;

        await Task.Run(async () =>
        {
            try
            {
                var topFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
                    .ToList();

                List<string> files;
                if (recursive)
                {
                    files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
                        .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                        .ToList();
                }
                else
                {
                    files = topFiles.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).ToList();
                }

                foreach (var file in files)
                {
                    var clip = new GameClip
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        DirectoryPath = Path.GetDirectoryName(file) ?? string.Empty
                    };

                    await _metadataService.PopulateMetadataAsync(clip);
                    clips.Add(clip);
                }
            }
            catch (Exception)
            {
            }
        });

        return clips;
    }

    public async Task<List<DirectoryNode>> BuildDirectoryTreesAsync(IEnumerable<string> watchRoots)
    {
        var roots = new List<DirectoryNode>();

        await Task.Run(() =>
        {
            foreach (var rootPath in watchRoots)
            {
                if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                    continue;

                try
                {
                    var node = CreateDirectoryNode(rootPath, isWatchRoot: true);
                    roots.Add(node);
                }
                catch (Exception)
                {
                }
            }
        });

        return roots;
    }

    private DirectoryNode CreateDirectoryNode(string path, bool isWatchRoot)
    {
        string folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName)) folderName = path;

        var node = new DirectoryNode
        {
            FullPath = path,
            Name = folderName,
            IsWatchRoot = isWatchRoot
        };

        try
        {
            int directClips = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly))
            {
                if (VideoExtensions.Contains(Path.GetExtension(file)))
                    directClips++;
            }

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var subNode = CreateDirectoryNode(dir, isWatchRoot: false);
                    if (subNode.ClipCount > 0 || subNode.SubDirectories.Count > 0)
                    {
                        node.SubDirectories.Add(subNode);
                    }
                }
                catch (Exception)
                {
                }
            }

            int subClipsTotal = node.SubDirectories.Sum(s => s.ClipCount);
            node.ClipCount = directClips > 0 ? directClips : subClipsTotal;
        }
        catch (Exception)
        {
        }

        return node;
    }

    public void UpdateWatchers(IEnumerable<string> watchRoots)
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();

            foreach (var root in watchRoots)
            {
                if (!Directory.Exists(root)) continue;

                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += (s, e) =>
                    {
                        if (VideoExtensions.Contains(Path.GetExtension(e.FullPath)))
                        {
                            // Debounce for write completion
                            Task.Delay(1000).ContinueWith(_ => ClipAdded?.Invoke(e.FullPath));
                        }
                    };

                    watcher.Deleted += (s, e) =>
                    {
                        if (VideoExtensions.Contains(Path.GetExtension(e.FullPath)))
                        {
                            ClipDeleted?.Invoke(e.FullPath);
                        }
                    };

                    watcher.Renamed += (s, e) =>
                    {
                        if (VideoExtensions.Contains(Path.GetExtension(e.FullPath)))
                        {
                            ClipRenamed?.Invoke(e.OldFullPath, e.FullPath);
                        }
                    };

                    _watchers.Add(watcher);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    public bool DeleteClipToRecycleBin(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var shf = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = filePath + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            };

            int result = SHFileOperation(ref shf);
            return result == 0;
        }
        catch
        {
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool RenameClip(string filePath, string newFileName, out string newPath)
    {
        newPath = filePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || string.IsNullOrWhiteSpace(newFileName))
            return false;

        try
        {
            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string ext = Path.GetExtension(filePath);
            if (!newFileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                newFileName += ext;
            }

            string target = Path.Combine(dir, newFileName);
            if (File.Exists(target) && !string.Equals(filePath, target, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            File.Move(filePath, target);
            newPath = target;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }

    #region Win32 Shell API
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);
    #endregion
}
