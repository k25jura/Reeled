using System;
using System.Collections.Generic;
using System.Linq;
using Reeled.Models;

namespace Reeled.Services;

public class NavigationService : INavigationService
{
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
}
