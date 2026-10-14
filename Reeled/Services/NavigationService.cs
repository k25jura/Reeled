using System;
using System.Collections.Generic;
using System.Linq;
using Reeled.Models;

namespace Reeled.Services;

public class NavigationService : INavigationService
{
    public static IReadOnlyCollection<string> SupportedVideoExtensions => Helpers.PathUtil.VideoExtensions;

    private readonly IClipMetadataService _metadataService;

    public NavigationService(IClipMetadataService metadataService)
    {
        _metadataService = metadataService;
    }

    public static bool IsVideoFilePath(string path) => Helpers.PathUtil.IsVideoFile(path);

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

        // Reuse the library's instance when the file is already indexed so favorites and moments stay in sync
        var library = App.GetService<ViewModels.HomeViewModel>();
        var clips = new List<GameClip>();
        foreach (var path in validPaths)
        {
            try
            {
                var fi = new System.IO.FileInfo(path);
                if (!fi.Exists) continue;
                clips.Add(library.ResolveOrCreateClip(fi));
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
                if (clip.Duration > TimeSpan.Zero) continue;
                try
                {
                    await _metadataService.PopulateMetadataAsync(clip);
                }
                catch { }
            }
        });
    }
}
