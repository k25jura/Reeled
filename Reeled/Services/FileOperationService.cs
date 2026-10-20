using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Reeled.Helpers;

namespace Reeled.Services;

/// <summary>A folder or file with that name is already there.</summary>
public sealed class NameTakenException : IOException
{
    public NameTakenException(string path) : base($"'{path}' already exists.") { }
}

/// <summary>A file that was meant to be replaced couldn't be sent to the Recycle Bin, so nothing was overwritten.</summary>
public sealed class RecycleFailedException : IOException
{
    public RecycleFailedException(string path) : base($"'{path}' couldn't be moved to the Recycle Bin.") { }
}

public sealed class FileOperationService : IFileOperationService
{
    private const int CopyBufferSize = 1 << 20;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(90);

    public async Task<FileTransferResult> TransferAsync(
        IReadOnlyList<string> sources,
        string destinationFolder,
        FileTransferKind kind,
        Func<FileConflict, Task<ConflictDecision>> resolveConflict,
        IProgress<FileTransferProgress>? progress,
        Action<TransferredFile>? onFileDone,
        CancellationToken cancellationToken)
    {
        var result = new FileTransferResult();
        var files = sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!Directory.Exists(destinationFolder))
        {
            foreach (var file in files) result.Failed.Add((file, new DirectoryNotFoundException()));
            return result;
        }

        long total = files.Sum(SafeLength);
        long doneBytes = 0;
        ConflictDecision? sticky = null;

        for (int i = 0; i < files.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }

            string source = files[i];
            string name = Path.GetFileName(source);
            long length = SafeLength(source);
            int index = i;
            progress?.Report(new FileTransferProgress(name, doneBytes, total, index, files.Count));

            try
            {
                if (!File.Exists(source)) throw new FileNotFoundException(null, source);

                string destination = Path.Combine(destinationFolder, name);
                bool replace = false;

                if (PathUtil.PathEquals(Path.GetDirectoryName(source), destinationFolder))
                {
                    // Pasting or dropping into the folder the file is already in
                    if (kind == FileTransferKind.Move)
                    {
                        result.Skipped++;
                        continue;
                    }
                    destination = UniquePath(destination);
                }
                else if (File.Exists(destination) || Directory.Exists(destination))
                {
                    var decision = sticky ?? await resolveConflict(new FileConflict(source, destination));
                    if (decision.ApplyToAll) sticky = decision;

                    if (decision.Choice == ConflictChoice.Skip)
                    {
                        result.Skipped++;
                        continue;
                    }
                    if (decision.Choice == ConflictChoice.Replace && File.Exists(destination))
                    {
                        replace = true;
                    }
                    else
                    {
                        destination = UniquePath(destination);
                    }
                }

                long before = doneBytes;
                void Report(long bytes) =>
                    progress?.Report(new FileTransferProgress(name, before + bytes, total, index, files.Count));

                await Task.Run(() => TransferOne(source, destination, kind, replace, length, Report, cancellationToken), cancellationToken);

                var done = new TransferredFile(source, destination);
                result.Done.Add(done);
                try
                {
                    onFileDone?.Invoke(done);
                }
                catch (Exception ex)
                {
                    Log.Error("FileTransfer onFileDone", ex);
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                Log.Error("FileTransfer", ex);
                result.Failed.Add((source, ex));
            }
            finally
            {
                doneBytes += length;
            }
        }

        progress?.Report(new FileTransferProgress(string.Empty, total, total, files.Count, files.Count));
        return result;
    }

    private static void TransferOne(string source, string destination, FileTransferKind kind, bool replace, long length, Action<long> report, CancellationToken ct)
    {
        bool sameVolume = string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase);
        if (kind == FileTransferKind.Move && sameVolume)
        {
            // A rename: instant, and File.Move refuses to overwrite, so a late name clash fails instead of destroying a file
            if (replace) RecycleExisting(destination);
            File.Move(source, destination);
            report(length);
            return;
        }

        // Written under a temporary name and renamed when complete, so a half-copied file is never taken for a clip
        string temp = destination + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        try
        {
            CopyContents(source, temp, report, ct);
            File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(source));
            ct.ThrowIfCancellationRequested();
            if (replace) RecycleExisting(destination);
            File.Move(temp, destination);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }

        if (kind == FileTransferKind.Move)
        {
            // The move of a file between drives is a copy followed by removing the original, which only happens
            // once the copy is known to be complete
            if (new FileInfo(destination).Length != length)
            {
                throw new IOException($"The copy of '{Path.GetFileName(source)}' is incomplete; the original was kept.");
            }
            File.Delete(source);
        }
    }

    private static void CopyContents(string source, string destination, Action<long> report, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan);
            long copied = 0;
            var clock = Stopwatch.StartNew();
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                copied += read;
                if (clock.Elapsed >= ProgressInterval)
                {
                    report(copied);
                    clock.Restart();
                }
            }
            report(copied);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void RecycleExisting(string path)
    {
        if (!RecycleCore(new[] { path }, IntPtr.Zero) || File.Exists(path))
        {
            throw new RecycleFailedException(path);
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static readonly Regex NumberedName = new(@"^(?<base>.*) \((?<n>\d+)\)$", RegexOptions.Compiled);

    /// <summary>"clip.mp4" taken: "clip (2).mp4", then "clip (3).mp4".</summary>
    public static string UniquePath(string path)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string extension = Path.GetExtension(path);
        string stem = Path.GetFileNameWithoutExtension(path);
        var match = NumberedName.Match(stem);
        if (match.Success) stem = match.Groups["base"].Value;

        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Folders
    // ---------------------------------------------------------------------------------------------

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public FileNameProblem? ValidateName(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return FileNameProblem.Empty;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return FileNameProblem.InvalidCharacters;
        if (name.EndsWith('.') || ReservedNames.Contains(Path.GetFileNameWithoutExtension(name))) return FileNameProblem.Reserved;
        return null;
    }

    public string CreateFolder(string parentFolder, string name)
    {
        name = name.Trim();
        if (ValidateName(name) != null) throw new ArgumentException("Not a valid folder name.", nameof(name));

        string path = Path.Combine(parentFolder, name);
        if (Directory.Exists(path) || File.Exists(path)) throw new NameTakenException(path);
        Directory.CreateDirectory(path);
        return path;
    }

    public string RenameFolder(string folderPath, string newName)
    {
        newName = newName.Trim();
        if (ValidateName(newName) != null) throw new ArgumentException("Not a valid folder name.", nameof(newName));

        string current = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetDirectoryName(current) ?? throw new IOException("A drive can't be renamed.");
        string target = Path.Combine(parent, newName);

        if (string.Equals(current, target, StringComparison.Ordinal)) return current;

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            // Only the letter case changes, which Directory.Move rejects: go through a temporary name
            string temp = Path.Combine(parent, newName + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            Directory.Move(current, temp);
            Directory.Move(temp, target);
            return target;
        }

        if (Directory.Exists(target) || File.Exists(target)) throw new NameTakenException(target);
        Directory.Move(current, target);
        return target;
    }

    // ---------------------------------------------------------------------------------------------
    // Recycle Bin
    // ---------------------------------------------------------------------------------------------

    public Task<bool> MoveToRecycleBinAsync(IReadOnlyList<string> paths, IntPtr owner) =>
        Task.Run(() => RecycleCore(paths, owner));

    /// <summary>
    /// One shell call for all paths. Without FOF_NOCONFIRMATION and with FOF_WANTNUKEWARNING, anything that
    /// the Recycle Bin can't take (too large, a network drive) brings up Windows' own warning instead of being
    /// deleted for good in silence.
    /// </summary>
    private static bool RecycleCore(IReadOnlyList<string> paths, IntPtr owner)
    {
        var existing = paths.Where(p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p))).ToList();
        if (existing.Count == 0) return true;

        try
        {
            var operation = new SHFILEOPSTRUCT
            {
                hwnd = owner,
                wFunc = FO_DELETE,
                pFrom = string.Join('\0', existing) + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING
            };
            int code = SHFileOperation(ref operation);
            return code == 0 && !operation.fAnyOperationsAborted && !existing.Any(p => File.Exists(p) || Directory.Exists(p));
        }
        catch (Exception ex)
        {
            // Never fall back to a permanent delete
            Log.Error("MoveToRecycleBin", ex);
            return false;
        }
    }

    #region Win32 Shell API
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

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
