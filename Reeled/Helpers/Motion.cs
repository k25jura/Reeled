using System;

namespace Reeled.Helpers;

/// <summary>
/// Single source of truth for "should this animate?". Motion is reduced when the in-app
/// Reduce Motion setting is on, or when Windows "Animation effects" is turned off.
/// </summary>
public static class Motion
{
    private static readonly Windows.UI.ViewManagement.UISettings UiSettings = new();

    public static bool IsReduced
    {
        get
        {
            try
            {
                if (App.Services != null &&
                    App.GetService<Services.ILocalStorageService>().CurrentSettings.ReduceMotion)
                {
                    return true;
                }
                return !UiSettings.AnimationsEnabled;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Starts a storyboard, collapsing it to (almost) zero time when motion is reduced so the
    /// final values and Completed handlers still apply exactly as they would after the animation.
    /// </summary>
    public static void Begin(Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard)
    {
        storyboard.Begin();
        if (IsReduced)
        {
            // Jump straight to the end values; Completed still fires. (Setting a large SpeedRatio
            // instead crashes inside Microsoft.UI.Xaml with a stowed exception.)
            try
            {
                storyboard.SkipToFill();
            }
            catch
            {
                // A storyboard that already finished or was stopped synchronously has nothing to skip
            }
        }
    }

    /// <summary>Returns the duration to use, or a near-zero duration when motion is reduced.</summary>
    public static TimeSpan Duration(double milliseconds) =>
        IsReduced ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(milliseconds);
}
