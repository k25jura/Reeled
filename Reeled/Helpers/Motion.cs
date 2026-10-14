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
            // Collapse every child to ~1ms so the storyboard still runs to completion and raises
            // Completed (much UI state is finalized there). SkipToFill() doesn't raise Completed, and a
            // huge SpeedRatio crashes inside Microsoft.UI.Xaml.
            foreach (var child in storyboard.Children)
            {
                Collapse(child);
            }
        }
        storyboard.Begin();
    }

    private static readonly Microsoft.UI.Xaml.Duration Instant =
        new(TimeSpan.FromMilliseconds(1));

    private static readonly Microsoft.UI.Xaml.Media.Animation.KeyTime InstantKeyTime =
        Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1));

    private static void Collapse(Microsoft.UI.Xaml.Media.Animation.Timeline timeline)
    {
        timeline.BeginTime = TimeSpan.Zero;
        switch (timeline)
        {
            case Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames d:
                // Same KeyTime for all frames: the last frame's value wins, i.e. the end state
                foreach (var frame in d.KeyFrames) frame.KeyTime = InstantKeyTime;
                break;
            case Microsoft.UI.Xaml.Media.Animation.ColorAnimationUsingKeyFrames c:
                foreach (var frame in c.KeyFrames) frame.KeyTime = InstantKeyTime;
                break;
            case Microsoft.UI.Xaml.Media.Animation.ObjectAnimationUsingKeyFrames o:
                foreach (var frame in o.KeyFrames) frame.KeyTime = InstantKeyTime;
                break;
            case Microsoft.UI.Xaml.Media.Animation.PointAnimationUsingKeyFrames p:
                foreach (var frame in p.KeyFrames) frame.KeyTime = InstantKeyTime;
                break;
            default:
                if (timeline.RepeatBehavior.Type != Microsoft.UI.Xaml.Media.Animation.RepeatBehaviorType.Forever)
                {
                    timeline.Duration = Instant;
                }
                break;
        }
    }

    /// <summary>Returns the duration to use, or a near-zero duration when motion is reduced.</summary>
    public static TimeSpan Duration(double milliseconds) =>
        IsReduced ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(milliseconds);
}
