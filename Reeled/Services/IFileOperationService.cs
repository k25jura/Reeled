using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Reeled.Services;

public enum FileTransferKind
{
    Copy,
    Move
}

public enum ConflictChoice
{
    /// <summary>Replace the existing file. It is sent to the Recycle Bin first, never overwritten.</summary>
    Replace,
    /// <summary>Keep both: the incoming file gets a " (2)" style name.</summary>
    KeepBoth,
    Skip
}

/// <summary>A file in the destination folder already has the incoming file's name.</summary>
public sealed record FileConflict(string SourcePath, string DestinationPath);

public sealed record ConflictDecision(ConflictChoice Choice, bool ApplyToAll);

public sealed record FileTransferProgress(string CurrentName, long BytesDone, long BytesTotal, int ItemIndex, int ItemCount);

public sealed record TransferredFile(string Source, string Destination);

public sealed class FileTransferResult
{
    public List<TransferredFile> Done { get; } = new();
    public List<(string Path, Exception Error)> Failed { get; } = new();
    public int Skipped { get; set; }
    public bool Cancelled { get; set; }
}

/// <summary>
/// Copies, moves and removes files for the library. Nothing here ever deletes user data for good: files
/// that are replaced or removed go to the Recycle Bin.
/// </summary>
public interface IFileOperationService
{
    /// <summary>
    /// Copies or moves files into a folder, one after the other, off the calling thread. Name clashes are put to
    /// <paramref name="resolveConflict"/> (and never overwritten silently). <paramref name="onFileDone"/> and the
    /// conflict callback run on the caller's synchronization context, so they can touch the UI.
    /// </summary>
    Task<FileTransferResult> TransferAsync(
        IReadOnlyList<string> sources,
        string destinationFolder,
        FileTransferKind kind,
        Func<FileConflict, Task<ConflictDecision>> resolveConflict,
        IProgress<FileTransferProgress>? progress,
        Action<TransferredFile>? onFileDone,
        CancellationToken cancellationToken);

    /// <summary>Sends files and folders to the Recycle Bin in one call. Returns false if any of them is still there afterwards.</summary>
    Task<bool> MoveToRecycleBinAsync(IReadOnlyList<string> paths, IntPtr owner);

    /// <summary>Creates a folder and returns its path. Throws <see cref="System.IO.IOException"/> if it can't be created.</summary>
    string CreateFolder(string parentFolder, string name);

    /// <summary>Renames a folder and returns its new path. Throws if the name is taken or the folder is in use.</summary>
    string RenameFolder(string folderPath, string newName);

    /// <summary>Why a file or folder name can't be used, or null when it is fine.</summary>
    FileNameProblem? ValidateName(string name);
}

public enum FileNameProblem
{
    Empty,
    InvalidCharacters,
    Reserved
}
