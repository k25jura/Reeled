using System;
using System.Collections.Generic;
using System.Linq;
using Reeled.Models;

namespace Reeled.Services;

public class NavigationService : INavigationService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".wmv", ".flv", ".ts", ".3gp"
    };

    public static IReadOnlyCollection<string> SupportedVideoExtensions => VideoExtensions;

    private readonly IClipMetadataService _metadataService;

    public NavigationService(IClipMetadataService metadataService)
    {
        _metadataService = metadataService;
    }

    public static bool IsVideoFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = System.IO.Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }

    public event Action<GameClip, List<GameClip>>? NavigatedToPlayer;
    public event Action? NavigatedToHome;
    public event Action? NavigatedToSettings;

    public void NavigateToPlayer(GameClip clip, IEnumerable<GameClip> playlist)
    {
        NavigatedToPlayer?.Invoke(clip, playlist.ToList());
    }

    public void NavigateToHome()
    {
        NavigatedToHome?.Invoke();
    }

    public void NavigateToSettings()
    {
        NavigatedToSettings?.Invoke();
    }

    public async System.Threading.Tasks.Task OpenVideoFilesAsync(IEnumerable<string> filePaths)
    {
        var validPaths = filePaths.Where(IsVideoFilePath).ToList();
        if (validPaths.Count == 0) return;

        var clips = new List<GameClip>();
        foreach (var path in validPaths)
        {
            try
            {
                var fi = new System.IO.FileInfo(path);
                if (!fi.Exists) continue;
                var clip = new GameClip
                {
                    FilePath = fi.FullName,
                    FileName = fi.Name,
                    DirectoryPath = fi.DirectoryName ?? string.Empty,
                    ModifiedDate = fi.LastWriteTimeUtc,
                    CreatedDate = fi.CreationTimeUtc,
                    FileSizeBytes = fi.Length
                };
                clips.Add(clip);
            }
            catch { }
        }

        if (clips.Count == 0) return;

        var firstClip = clips[0];
        NavigateToPlayer(firstClip, clips);

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            foreach (var clip in clips)
            {
                try
                {
                    await _metadataService.PopulateMetadataAsync(clip);
                }
                catch { }
            }
        });
    }
}
