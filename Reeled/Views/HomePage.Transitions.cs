using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Reeled.Views;

/// <summary>
/// Transitions of the clip area: the loading skeleton crossfades into the clips (cards or list rows), and
/// a change of sort order fades the items out and back in instead of letting them jump.
/// </summary>
public sealed partial class HomePage
{
    // Whichever storyboard currently owns the opacity/offset of the clips view (load crossfade, sort fade)
    private Storyboard? _contentStoryboard;
    private bool? _loadingShown;
    private bool _sortFading;
    private int _pendingSort = -1;
    private readonly HashSet<Grid> _skeletonRows = new();

    private FrameworkElement ActiveSkeleton => IsListMode ? ListSkeletonScrollViewer : SkeletonScrollViewer;

    private TranslateTransform ActiveContentTranslate => IsListMode ? ClipsListTranslate : ClipsGridTranslate;

    // ---------------------------------------------------------------------------------------------
    // Skeleton <-> content
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Shows the skeleton that matches the view mode while loading, and crossfades it into the clips when
    /// loading ends (the clips rise a few pixels as they come in). Visibility is owned here rather than
    /// bound, so nothing pops.
    /// </summary>
    private void ApplyLoadingState(bool animate, bool force = false)
    {
        if (SkeletonScrollViewer == null || ListSkeletonScrollViewer == null) return;
        bool loading = ViewModel.ShowSkeletonLoading;
        if (!force && _loadingShown == loading) return;
        _loadingShown = loading;

        // Take over from a sort fade in flight: make the sort it was waiting to apply
        _contentStoryboard?.Stop();
        _contentStoryboard = null;
        _sortFading = false;
        int pending = _pendingSort;
        _pendingSort = -1;
        if (pending >= 0 && pending != ViewModel.SortIndex) ViewModel.SortIndex = pending;
        _contentStoryboard?.Stop();
        _contentStoryboard = null;

        ResetClipsVisuals();

        var skeleton = ActiveSkeleton;
        var content = ActiveClipsView;
        var translate = ActiveContentTranslate;
        // The other mode's views never show alongside this one
        (IsListMode ? SkeletonScrollViewer : ListSkeletonScrollViewer).Visibility = Visibility.Collapsed;
        (IsListMode ? ClipsGridView : (UIElement)ClipsListView).Visibility = loading ? Visibility.Collapsed : Visibility.Visible;

        if (loading)
        {
            content.Visibility = Visibility.Collapsed;
            skeleton.Visibility = Visibility.Visible;
            UpdateSkeletonAnimations();
            return;
        }

        bool crossfade = animate && !Helpers.Motion.IsReduced && skeleton.Visibility == Visibility.Visible;
        content.Visibility = Visibility.Visible;
        if (!crossfade)
        {
            skeleton.Visibility = Visibility.Collapsed;
            UpdateSkeletonAnimations();
            return;
        }

        content.Opacity = 0;
        translate.Y = 10;
        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(skeleton, "Opacity", 0, Helpers.Motion.Short, from: 1, accelerate: true));
        sb.Children.Add(Helpers.Motion.Spline(content, "Opacity", 1, Helpers.Motion.Medium, from: 0, delayMs: 80));
        sb.Children.Add(Helpers.Motion.Spline(translate, "Y", 0, Helpers.Motion.Long, from: 10, delayMs: 80));
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_contentStoryboard, sb)) return;
            _contentStoryboard = null;
            sb.Stop();
            ResetClipsVisuals();
            skeleton.Visibility = Visibility.Collapsed;
            UpdateSkeletonAnimations();
        };
        _contentStoryboard = sb;
        Helpers.Motion.BeginExact(sb);
    }

    /// <summary>Back to the resting state of every view that takes part in a transition.</summary>
    private void ResetClipsVisuals()
    {
        ClipsGridView.Opacity = 1;
        ClipsGridTranslate.Y = 0;
        ClipsListView.Opacity = 1;
        ClipsListTranslate.Y = 0;
        SkeletonScrollViewer.Opacity = 1;
        ListSkeletonScrollViewer.Opacity = 1;
    }

    // ---------------------------------------------------------------------------------------------
    // List skeleton rows follow the list's column layout
    // ---------------------------------------------------------------------------------------------

    private void OnSkeletonRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid row && _skeletonRows.Add(row)) ApplySkeletonRowColumns(row);
        OnSkeletonCardLoaded(sender, e);
    }

    private void OnSkeletonRowUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid row) _skeletonRows.Remove(row);
    }

    private void ApplySkeletonRowColumns(Grid row)
    {
        ApplyColumns(row);
        if (row.FindName("SkeletonSubtitle") is UIElement subtitle) subtitle.Visibility = _listCompact ? Visibility.Visible : Visibility.Collapsed;
        if (row.FindName("SkeletonFolder") is UIElement folder) folder.Visibility = _listCompact ? Visibility.Collapsed : Visibility.Visible;
        if (row.FindName("SkeletonDate") is UIElement date) date.Visibility = _listCompact ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------------------------------------
    // Sorting
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Changes the sort order behind a quick fade: the items fade out, the order changes while they are
    /// hidden, and they fade back in (see <see cref="PlayContentEnter"/>). Repeated requests while the
    /// fade-out runs just retarget it.
    /// </summary>
    private void ChangeSortAnimated(int sortIndex)
    {
        var content = ActiveClipsView;
        bool animate = !Helpers.Motion.IsReduced && !ViewModel.ShowSkeletonLoading &&
                       content.Visibility == Visibility.Visible && content.ActualHeight > 0 &&
                       ViewModel.FilteredClips.Count > 1;
        if (!animate)
        {
            if (ViewModel.SortIndex == sortIndex) ScrollClipsToTop();
            else ViewModel.SortIndex = sortIndex;
            return;
        }

        if (_sortFading)
        {
            _pendingSort = sortIndex;
            return;
        }
        if (sortIndex == ViewModel.SortIndex) return;

        _pendingSort = sortIndex;
        _sortFading = true;
        _contentStoryboard?.Stop();
        var translate = ActiveContentTranslate;
        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(content, "Opacity", 0, Helpers.Motion.Micro, from: content.Opacity, accelerate: true));
        sb.Children.Add(Helpers.Motion.Spline(translate, "Y", -4, Helpers.Motion.Micro, from: translate.Y, accelerate: true));
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_contentStoryboard, sb)) return;
            _sortFading = false;
            int target = _pendingSort;
            _pendingSort = -1;
            if (target < 0 || target == ViewModel.SortIndex) PlayContentEnter();
            else ViewModel.SortIndex = target; // the change handler fades the new order in
        };
        _contentStoryboard = sb;
        Helpers.Motion.BeginExact(sb);
    }

    /// <summary>Fades the clips in from slightly below; used once a new sort order is in place.</summary>
    private void PlayContentEnter()
    {
        if (ViewModel.ShowSkeletonLoading) return;
        var content = ActiveClipsView;
        if (content.Visibility != Visibility.Visible) return;

        _contentStoryboard?.Stop();
        _contentStoryboard = null;
        var translate = ActiveContentTranslate;
        if (Helpers.Motion.IsReduced)
        {
            content.Opacity = 1;
            translate.Y = 0;
            return;
        }

        content.Opacity = 0;
        translate.Y = 8;
        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(content, "Opacity", 1, Helpers.Motion.Short, from: 0));
        sb.Children.Add(Helpers.Motion.Spline(translate, "Y", 0, Helpers.Motion.Medium, from: 8));
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_contentStoryboard, sb)) return;
            _contentStoryboard = null;
            sb.Stop();
            content.Opacity = 1;
            translate.Y = 0;
        };
        _contentStoryboard = sb;
        Helpers.Motion.BeginExact(sb);
    }
}
