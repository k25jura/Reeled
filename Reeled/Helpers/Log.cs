using System;
using System.IO;

namespace Reeled.Helpers;

/// <summary>
/// Minimal file logger. Writes to %LOCALAPPDATA%\Reeled\Logs\reeled.log so logging works
/// regardless of the working directory (e.g. System32 when launched via "Open with").
/// </summary>
public static class Log
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private static readonly object Sync = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reeled", "Logs");

    public static string LogFilePath { get; } = Path.Combine(LogDirectory, "reeled.log");

    public static void Error(string context, Exception? ex) =>
        Write($"[{context}] {ex}");

    public static void Error(string context, string message) =>
        Write($"[{context}] {message}");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var info = new FileInfo(LogFilePath);
                if (info.Exists && info.Length > MaxLogBytes)
                {
                    File.Move(LogFilePath, LogFilePath + ".old", overwrite: true);
                }
                File.AppendAllText(LogFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never throw
        }
    }
}
