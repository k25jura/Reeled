using System;
using System.Collections.Generic;
using System.IO;

namespace Reeled.Helpers;

public static class PathUtil
{
    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".wmv", ".flv", ".ts", ".3gp"
    };

    public static bool IsVideoFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }

    /// <summary>
    /// True when <paramref name="path"/> equals <paramref name="root"/> or lives anywhere beneath it.
    /// Unlike a plain StartsWith, "C:\Clips2\a.mp4" is not considered under "C:\Clips".
    /// </summary>
    public static bool IsUnder(string? path, string? root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
        string trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (path.Length == trimmedRoot.Length)
        {
            return string.Equals(path, trimmedRoot, StringComparison.OrdinalIgnoreCase);
        }
        return path.Length > trimmedRoot.Length
               && path.StartsWith(trimmedRoot, StringComparison.OrdinalIgnoreCase)
               && (path[trimmedRoot.Length] == Path.DirectorySeparatorChar || path[trimmedRoot.Length] == Path.AltDirectorySeparatorChar);
    }

    public static bool PathEquals(string? a, string? b) =>
        string.Equals(
            a?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
