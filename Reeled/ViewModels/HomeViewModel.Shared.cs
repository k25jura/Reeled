using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Reeled.Services.Sharing;

namespace Reeled.ViewModels;

/// <summary>
/// The Shared section: clips that have been shared, like Favorites and Saved Moments are clips with a heart
/// or with moments. Each clip's share count drives the badge on its card and row.
/// </summary>
public partial class HomeViewModel
{
    public bool IsSharedSelected => CurrentSection == NavigationSection.Shared;

    public int SharedCount => AllClips.Count(c => c.IsShared);

    private bool _shareHistoryHooked;

    /// <summary>Keeps share badges and the Shared section in step with the share history.</summary>
    private void HookShareHistory()
    {
        if (_shareHistoryHooked) return;
        _shareHistoryHooked = true;
        App.GetService<ShareService>().HistoryChanged += path => _dispatcherQueue?.TryEnqueue(() => OnShareHistoryChanged(path));
    }

    private void OnShareHistoryChanged(string? clipPath)
    {
        var shares = App.GetService<ShareService>();
        if (clipPath != null)
        {
            var clip = FindClip(clipPath);
            if (clip != null) clip.ShareCount = shares.CountFor(clipPath);
        }
        else
        {
            foreach (var clip in AllClips) clip.ShareCount = shares.CountFor(clip.FilePath);
        }

        OnPropertyChanged(nameof(SharedCount));
        if (CurrentSection == NavigationSection.Shared)
        {
            // The section's base set: a clip whose last share was removed leaves it, a newly shared one joins
            var shared = AllClips.Where(c => c.IsShared).ToList();
            if (shared.Count != Clips.Count || shared.Any(c => !Clips.Contains(c)))
            {
                Clips.ReplaceRange(shared);
                ApplyFilterAndSort();
            }
            UpdateCounts();
            OnPropertyChanged(nameof(CurrentDirectoryPath));
        }
    }

    [RelayCommand]
    public Task SelectSharedAsync()
    {
        CurrentSection = NavigationSection.Shared;
        SelectedDirectory = null;
        RefreshCurrentViewClips();
        return Task.CompletedTask;
    }
}
