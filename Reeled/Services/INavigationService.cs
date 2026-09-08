using System;
using System.Collections.Generic;
using Reeled.Models;

namespace Reeled.Services;

public interface INavigationService
{
    event Action<GameClip, List<GameClip>>? NavigatedToPlayer;
    event Action? NavigatedToHome;
    event Action? NavigatedToSettings;

    void NavigateToPlayer(GameClip clip, IEnumerable<GameClip> playlist);
    void NavigateToHome();
    void NavigateToSettings();
}
