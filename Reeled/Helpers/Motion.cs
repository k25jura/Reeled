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
        if (IsReduced)
        {
            storyboard.SpeedRatio = 1000;
        }
        storyboard.Begin();
    }

    /// <summary>Returns the duration to use, or a near-zero duration when motion is reduced.</summary>
    public static TimeSpan Duration(double milliseconds) =>
        IsReduced ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(milliseconds);
}
