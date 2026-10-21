using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Helpers;
using Reeled.Models;

namespace Reeled.Services;

public class ClipIndexerService : IClipIndexerService
{
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LibraryChangedDebounce = TimeSpan.FromMilliseconds(800);

    private readonly IClipMetadataService _metadataService;
    private readonly IClipMetadataCacheService _cacheService;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly object _lock = new();

    // Paths waiting for the recorder to release the file, so each new clip is announced exactly once
    private readonly ConcurrentDictionary<string, byte> _pendingAdds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _watcherCts = new();
    private Timer? _libraryChangedTimer;

    public event Action<string>? ClipAdded;
    public event Action<string>? ClipDeleted;
    public event Action<string, string>? ClipRenamed;
    public event Action? LibraryChanged;
    public event Action? MetadataBatchCompleted;

    public ClipIndexerService(IClipMetadataService metadataService, IClipMetadataCacheService cacheService)
    {
        _metadataService = metadataService;
        _cacheService = cacheService;
    }

    public async Task<List<GameClip>> ScanDirectoryClipsAsync(string directoryPath, bool recursive = false)
    {
        var clips = new List<GameClip>();
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            return clips;

        await Task.Run(() =>
        {
            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = recursive,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System
                };
                var fileInfos = dirInfo.EnumerateFiles("*", options)
                    .Where(fi => PathUtil.VideoExtensions.Contains(fi.Extension))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .ToList();

                var uncachedClips = new List<GameClip>();
                foreach (var fileInfo in fileInfos)
                {
                    try
                    {
                        var clip = GameClip.FromFileInfo(fileInfo);

                        if (_cacheService.TryGet(clip.FilePath, fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, out var cached) && cached != null)
                        {
                            clip.Duration = TimeSpan.FromTicks(cached.DurationTicks);
                            clip.VideoWidth = cached.VideoWidth;
                            clip.VideoHeight = cached.VideoHeight;
                        }
                        else
                        {
                            uncachedClips.Add(clip);
                        }

                        clips.Add(clip);
                    }
                    catch { }
                }

                if (uncachedClips.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        foreach (var uncached in uncachedClips)
                        {
                            try
                            {
                                await _metadataService.PopulateMetadataAsync(uncached);
                            }
                            catch { }
                        }
                        await _cacheService.SaveAsync();
                        MetadataBatchCompleted?.Invoke();
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("ScanDirectoryClips", ex);
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
            var dirInfo = new DirectoryInfo(path);
            foreach (var fi in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                if (PathUtil.VideoExtensions.Contains(fi.Extension))
                    directClips++;
            }

            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                if ((subDir.Attributes & (FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
                    continue;

                try
                {
                    var subNode = CreateDirectoryNode(subDir.FullName, isWatchRoot: false);
                    // A folder with nothing in it is kept too: it's a place to move clips to (a new folder starts empty)
                    if (subNode.ClipCount > 0 || subNode.SubDirectories.Count > 0 || IsEmptyDirectory(subDir))
                    {
                        node.SubDirectories.Add(subNode);
                    }
                }
                catch (Exception)
                {
                }
            }

            int subClipsTotal = node.SubDirectories.Sum(s => s.ClipCount);
            node.ClipCount = directClips + subClipsTotal;
            node.HasSubDirectories = node.SubDirectories.Count > 0;
        }
        catch (Exception)
        {
        }

        return node;
    }

    private static bool IsEmptyDirectory(DirectoryInfo directory)
    {
        try
        {
            return !directory.EnumerateFileSystemInfos().Any();
        }
        catch
        {
            return false;
        }
    }

    public void UpdateWatchers(IEnumerable<string> watchRoots)
    {
        lock (_lock)
        {
            DisposeWatchers();
            _watcherCts = new CancellationTokenSource();

            // Watch only the outermost roots: nested roots (e.g. Videos + Videos\Captures) would otherwise
            // report every event twice.
            var distinctRoots = watchRoots
                .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var outermostRoots = distinctRoots
                .Where(r => !distinctRoots.Any(other => !PathUtil.PathEquals(other, r) && PathUtil.IsUnder(r, other)))
                .ToList();

            foreach (var root in outermostRoots)
            {
                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        IncludeSubdirectories = true,
                        InternalBufferSize = 64 * 1024
                    };

                    watcher.Created += OnWatcherCreated;
                    watcher.Deleted += OnWatcherDeleted;
                    watcher.Renamed += OnWatcherRenamed;
                    watcher.Error += (s, e) =>
                    {
                        Log.Error("FileSystemWatcher", e.GetException());
                        RaiseLibraryChangedDebounced();
                    };
                    watcher.EnableRaisingEvents = true;

                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    Log.Error("UpdateWatchers", ex);
                }
            }
        }
    }

    private void OnWatcherCreated(object sender, FileSystemEventArgs e)
    {
        if (PathUtil.IsVideoFile(e.FullPath))
        {
            QueueClipAdded(e.FullPath);
        }
        else if (Directory.Exists(e.FullPath))
        {
            RaiseLibraryChangedDebounced();
        }
    }

    private void OnWatcherDeleted(object sender, FileSystemEventArgs e)
    {
        if (PathUtil.IsVideoFile(e.FullPath))
        {
            _pendingAdds.TryRemove(e.FullPath, out _);
            ClipDeleted?.Invoke(e.FullPath);
        }
        else if (string.IsNullOrEmpty(Path.GetExtension(e.FullPath)))
        {
            // Most likely a directory: its clips disappear without individual events
            RaiseLibraryChangedDebounced();
        }
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        bool wasVideo = PathUtil.IsVideoFile(e.OldFullPath);
        bool isVideo = PathUtil.IsVideoFile(e.FullPath);

        if (wasVideo && isVideo)
        {
            if (_pendingAdds.TryRemove(e.OldFullPath, out _))
            {
                QueueClipAdded(e.FullPath);
            }
            else
            {
                ClipRenamed?.Invoke(e.OldFullPath, e.FullPath);
            }
        }
        else if (isVideo)
        {
            // Recorders often write to a temp name then rename to the final .mp4
            QueueClipAdded(e.FullPath);
        }
        else if (wasVideo)
        {
            _pendingAdds.TryRemove(e.OldFullPath, out _);
            ClipDeleted?.Invoke(e.OldFullPath);
        }
        else if (Directory.Exists(e.FullPath))
        {
            RaiseLibraryChangedDebounced();
        }
    }

    private void QueueClipAdded(string path)
    {
        if (!_pendingAdds.TryAdd(path, 0)) return;
        var token = _watcherCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait until the recorder has finished writing: the file must be openable exclusively
                // and its size stable between two polls.
                long lastSize = -1;
                while (!token.IsCancellationRequested && _pendingAdds.ContainsKey(path))
                {
                    await Task.Delay(ReadyPollInterval, token);
                    if (!File.Exists(path)) return;

                    long size = new FileInfo(path).Length;
                    if (size > 0 && size == lastSize && CanOpenExclusively(path))
                    {
                        if (_pendingAdds.TryRemove(path, out _))
                        {
                            ClipAdded?.Invoke(path);
                        }
                        return;
                    }
                    lastSize = size;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error("QueueClipAdded", ex);
            }
            finally
            {
                _pendingAdds.TryRemove(path, out _);
            }
        }, token);
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private long _libraryChangedSuppressedUntil;

    public void SuppressLibraryChanged(TimeSpan duration) =>
        Interlocked.Exchange(ref _libraryChangedSuppressedUntil, Environment.TickCount64 + (long)duration.TotalMilliseconds);

    private void RaiseLibraryChangedDebounced()
    {
        lock (_lock)
        {
            _libraryChangedTimer?.Dispose();
            _libraryChangedTimer = new Timer(_ =>
            {
                if (Environment.TickCount64 < Interlocked.Read(ref _libraryChangedSuppressedUntil)) return;
                LibraryChanged?.Invoke();
            }, null, LibraryChangedDebounce, Timeout.InfiniteTimeSpan);
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
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };

            int result = SHFileOperation(ref shf);
            return result == 0 && !shf.fAnyOperationsAborted;
        }
        catch (Exception ex)
        {
            // Never fall back to a permanent delete: the user asked for the Recycle Bin
            Log.Error("DeleteClipToRecycleBin", ex);
            return false;
        }
    }

    public bool RenameClip(string filePath, string newFileName, out string newPath)
    {
        newPath = filePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || string.IsNullOrWhiteSpace(newFileName))
            return false;

        newFileName = newFileName.Trim();
        if (newFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
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
        catch (Exception ex)
        {
            Log.Error("RenameClip", ex);
            return false;
        }
    }

    private void DisposeWatchers()
    {
        try { _watcherCts.Cancel(); } catch { }
        _pendingAdds.Clear();
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            DisposeWatchers();
            _libraryChangedTimer?.Dispose();
            _libraryChangedTimer = null;
        }
    }

    #region Win32 Shell API
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

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
