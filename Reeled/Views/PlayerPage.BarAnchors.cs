using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;

namespace Reeled.Views;

/// <summary>
/// Everything that floats just above the bottom bar (subtitles, the current-moment pill, the timeline
/// picker prompt, the moments card, the toolbar editor, a bottom-placed OSD) is positioned from the bar's
/// measured height plus one shared gap, so Classic (two rows) and Compact (one row) leave the same space.
/// </summary>
public sealed partial class PlayerPage
{
    /// <summary>Space between the top of the bar and whatever floats above it.</summary>
    private const double BarGap = 16;

    // Resting bottom margins declared in XAML for elements that also rest low when the bar hides
    private const double SubtitleBaseMargin = 40;
    private const double ActiveMomentBaseMargin = 48;

    // Measured on layout; a Classic-like default until the first measure
    private double _barHeight = 84;
    private Storyboard? _barAnchorStoryboard;

    private double BarClearance => _barHeight + BarGap;

    /// <summary>How far the current-moment pill rides up from its resting spot while the bar is showing.</summary>
    private double ActiveBadgeLift(bool controlsVisible) =>
        controlsVisible ? -Math.Max(0, BarClearance - ActiveMomentBaseMargin) : 0;

    private void InitializeBarAnchors()
    {
        BottomControlBar.SizeChanged += (s, e) =>
        {
            // The bar reports 0 while collapsed; only a real change of height (mode switch, wrapping) matters
            if (e.NewSize.Height <= 0 || Math.Abs(e.NewSize.Height - _barHeight) < 0.5) return;
            _barHeight = e.NewSize.Height;
            UpdateBarAnchors(animate: _isActive);
        };
        UpdateBarAnchors(animate: false);
    }

    /// <summary>Moves every bar-anchored element to match the bar's current height.</summary>
    private void UpdateBarAnchors(bool animate)
    {
        double clearance = BarClearance;
        double badgeY = ActiveBadgeLift(_areControlsShowing);

        AnimateSubtitleLift(_areControlsShowing, animate);

        // Not bar-height dependent in XAML any more: margins rest at the bottom edge, the transform lifts them
        if (_toolbarEditorOverlay != null)
        {
            _toolbarEditorOverlay.Margin = new Thickness(24, 0, 24, clearance + 12);
        }
        if (_osdAppliedPosition == OsdPosition.Bottom)
        {
            OsdContent.Margin = new Thickness(0, 0, 0, clearance + 16);
        }

        _barAnchorStoryboard?.Stop();
        _barAnchorStoryboard = null;

        if (!animate || Helpers.Motion.IsReduced)
        {
            ApplyBarAnchors(badgeY, clearance);
            return;
        }

        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(ActiveMomentBadgeTranslation, "Y", badgeY, Helpers.Motion.Medium));
        sb.Children.Add(Helpers.Motion.Spline(TimelinePickerBadgeTranslation, "Y", -clearance, Helpers.Motion.Medium));
        sb.Children.Add(Helpers.Motion.Spline(MomentsPopupTranslation, "TranslateY", -clearance, Helpers.Motion.Medium));
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_barAnchorStoryboard, sb)) return;
            _barAnchorStoryboard = null;
            // Land the real values and release the animation hold, so later direct assignments show
            ApplyBarAnchors(ActiveBadgeLift(_areControlsShowing), BarClearance);
            sb.Stop();
        };
        _barAnchorStoryboard = sb;
        Helpers.Motion.BeginExact(sb);
        _videoGlass?.Track((int)Helpers.Motion.Medium + 60);
    }

    private void ApplyBarAnchors(double badgeY, double clearance)
    {
        ActiveMomentBadgeTranslation.Y = badgeY;
        TimelinePickerBadgeTranslation.Y = -clearance;
        MomentsPopupTranslation.TranslateY = -clearance;
    }
}
