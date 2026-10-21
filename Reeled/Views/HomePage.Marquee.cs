using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Helpers;
using Reeled.Models;
using Windows.Foundation;

namespace Reeled.Views;

/// <summary>
/// Rubber-band selection like File Explorer: press on empty space in the clip view and drag. The rectangle is
/// anchored in content coordinates (so it stays put while the view scrolls), clips it touches are selected
/// live, Ctrl toggles against the selection that was there, and the view scrolls when the pointer nears its
/// top or bottom edge. Only clips that are realized by the virtualized view can be hit.
/// </summary>
public sealed partial class HomePage
{
    private const double MarqueeThreshold = 4;
    private const double MarqueeEdgeZone = 56;
    private const double MarqueeMaxSpeed = 30;

    private bool _mqCandidate;
    private bool _mqActive;
    private uint _mqPointerId;
    private bool _mqCtrl;
    private Point _mqStartHost;
    private double _mqAnchorX;
    private double _mqAnchorContentY;
    private Point _mqPos;
    private bool _mqDirty;
    private double _mqLastOffset = -1;
    private ScrollViewer? _mqScroll;
    private HashSet<GameClip> _mqBase = new();
    private readonly HashSet<GameClip> _mqHit = new();
    private Border? _mqRect;
    private Storyboard? _mqFade;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _mqTimer;

    /// <summary>Called once from the constructor.</summary>
    private void WireMarquee()
    {
        ClipsHost.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnClipsAreaPointerPressed), handledEventsToo: true);
        ClipsHost.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMarqueePointerMoved), handledEventsToo: true);
        ClipsHost.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnMarqueePointerReleased), handledEventsToo: true);
        ClipsHost.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnMarqueePointerCaptureLost), handledEventsToo: true);
    }

    private void BeginMarqueeCandidate(PointerRoutedEventArgs e, bool ctrl)
    {
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        var sv = ActiveScrollViewer();
        if (sv == null) return;

        var host = e.GetCurrentPoint(ClipsHost).Position;
        var view = ActiveClipsView;
        var origin = view.TransformToVisual(ClipsHost).TransformPoint(new Point(0, 0));
        // Only the clip area itself (not the list's column header)
        if (host.X < origin.X || host.Y < origin.Y || host.X > origin.X + view.ActualWidth || host.Y > origin.Y + view.ActualHeight) return;

        _mqCandidate = true;
        _mqPointerId = e.Pointer.PointerId;
        _mqCtrl = ctrl;
        _mqStartHost = host;
        _mqPos = host;
        _mqScroll = sv;
        _mqAnchorX = host.X;
        _mqAnchorContentY = host.Y + sv.VerticalOffset;
    }

    private void OnMarqueePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_mqCandidate || e.Pointer.PointerId != _mqPointerId) return;
        if (!e.GetCurrentPoint(ClipsHost).Properties.IsLeftButtonPressed)
        {
            EndMarquee();
            return;
        }
        _mqPos = e.GetCurrentPoint(ClipsHost).Position;
        if (!_mqActive)
        {
            double dx = _mqPos.X - _mqStartHost.X, dy = _mqPos.Y - _mqStartHost.Y;
            if (dx * dx + dy * dy < MarqueeThreshold * MarqueeThreshold) return;
            StartMarquee(e.Pointer);
        }
        _mqDirty = true;
    }

    private void OnMarqueePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _mqPointerId) return;
        EndMarquee();
    }

    private void OnMarqueePointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _mqPointerId) return;
        EndMarquee();
    }

    private void StartMarquee(Microsoft.UI.Xaml.Input.Pointer pointer)
    {
        _mqActive = true;
        _mqBase = _mqCtrl ? new HashSet<GameClip>(_selection) : new HashSet<GameClip>();
        _mqHit.Clear();
        _mqLastOffset = -1;
        try { ClipsHost.CapturePointer(pointer); } catch { }

        EnsureMarqueeRect();
        _mqFade?.Stop();
        _mqFade = null;
        _mqRect!.Opacity = 1;
        _mqRect.Visibility = Visibility.Collapsed;

        _mqTimer ??= DispatcherQueue.CreateTimer();
        _mqTimer.Interval = TimeSpan.FromMilliseconds(16);
        _mqTimer.IsRepeating = true;
        _mqTimer.Tick -= OnMarqueeTick;
        _mqTimer.Tick += OnMarqueeTick;
        _mqTimer.Start();
    }

    private void EndMarquee()
    {
        bool wasActive = _mqActive;
        _mqCandidate = false;
        _mqActive = false;
        _mqTimer?.Stop();
        _mqHit.Clear();
        _mqBase = new HashSet<GameClip>();
        try { ClipsHost.ReleasePointerCaptures(); } catch { }
        if (wasActive) FadeOutMarqueeRect();
    }

    private void EnsureMarqueeRect()
    {
        if (_mqRect != null) return;
        var rect = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Canvas.SetZIndex(rect, 5);
        ThemeBrushes.Track(rect, () =>
        {
            var accent = ThemeBrushes.Get("AccentFillColorDefaultBrush", rect) as SolidColorBrush;
            var c = accent?.Color ?? Microsoft.UI.Colors.DodgerBlue;
            rect.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, c.R, c.G, c.B));
            rect.BorderBrush = new SolidColorBrush(c);
        });
        ClipsHost.Children.Add(rect);
        _mqRect = rect;
    }

    private void FadeOutMarqueeRect()
    {
        var rect = _mqRect;
        if (rect == null || rect.Visibility != Visibility.Visible) return;
        if (Motion.IsReduced)
        {
            rect.Visibility = Visibility.Collapsed;
            return;
        }
        var story = new Storyboard();
        story.Children.Add(Motion.Spline(rect, "Opacity", 0, Motion.Micro, from: rect.Opacity, accelerate: true));
        story.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_mqFade, story)) return;
            _mqFade = null;
            story.Stop();
            rect.Visibility = Visibility.Collapsed;
            rect.Opacity = 1;
        };
        _mqFade = story;
        Motion.BeginExact(story);
    }

    private void OnMarqueeTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!_mqActive || _mqScroll == null)
        {
            sender.Stop();
            return;
        }

        var view = ActiveClipsView;
        var sv = _mqScroll;
        double viewTop = view.TransformToVisual(ClipsHost).TransformPoint(new Point(0, 0)).Y;
        double viewBottom = viewTop + view.ActualHeight;

        // Scroll while the pointer rests near the top or bottom edge (faster the closer to / beyond it)
        double speed = 0;
        if (_mqPos.Y < viewTop + MarqueeEdgeZone)
            speed = -Math.Min(1.6, (viewTop + MarqueeEdgeZone - _mqPos.Y) / MarqueeEdgeZone) * MarqueeMaxSpeed;
        else if (_mqPos.Y > viewBottom - MarqueeEdgeZone)
            speed = Math.Min(1.6, (_mqPos.Y - (viewBottom - MarqueeEdgeZone)) / MarqueeEdgeZone) * MarqueeMaxSpeed;
        if (speed != 0)
        {
            double target = Math.Clamp(sv.VerticalOffset + speed, 0, sv.ScrollableHeight);
            if (Math.Abs(target - sv.VerticalOffset) > 0.01) sv.ChangeView(null, target, null, disableAnimation: true);
        }

        // New clips realize while scrolling, so the test runs again whenever the offset moved
        if (!_mqDirty && Math.Abs(sv.VerticalOffset - _mqLastOffset) < 0.01) return;
        _mqDirty = false;
        _mqLastOffset = sv.VerticalOffset;
        UpdateMarquee(view, sv, viewTop, viewBottom);
    }

    private void UpdateMarquee(ListViewBase view, ScrollViewer sv, double viewTop, double viewBottom)
    {
        double off = sv.VerticalOffset;
        double viewLeft = view.TransformToVisual(ClipsHost).TransformPoint(new Point(0, 0)).X;
        double viewRight = viewLeft + view.ActualWidth;

        double curX = Math.Clamp(_mqPos.X, viewLeft, viewRight);
        double curY = Math.Clamp(_mqPos.Y, viewTop, viewBottom) + off;
        double left = Math.Min(_mqAnchorX, curX), right = Math.Max(_mqAnchorX, curX);
        double top = Math.Min(_mqAnchorContentY, curY), bottom = Math.Max(_mqAnchorContentY, curY);

        // The rectangle on screen: content coordinates minus the scroll offset, kept inside the clip area
        double sTop = Math.Max(top - off, viewTop), sBottom = Math.Min(bottom - off, viewBottom);
        if (_mqRect != null)
        {
            if (sBottom <= sTop || right <= left)
            {
                _mqRect.Visibility = Visibility.Collapsed;
            }
            else
            {
                _mqRect.Margin = new Thickness(left, sTop, 0, 0);
                _mqRect.Width = Math.Max(1, right - left);
                _mqRect.Height = Math.Max(1, sBottom - sTop);
                _mqRect.Visibility = Visibility.Visible;
            }
        }

        // Which realized clips does it touch?
        foreach (var clip in DisplayOrder())
        {
            if (view.ContainerFromItem(clip) is not FrameworkElement container || container.ActualWidth <= 0) continue;
            var p = container.TransformToVisual(ClipsHost).TransformPoint(new Point(0, 0));
            double cl = p.X, cr = p.X + container.ActualWidth;
            double ct = p.Y + off, cb = ct + container.ActualHeight;
            bool hit = cl < right && cr > left && ct < bottom && cb > top;
            if (hit) _mqHit.Add(clip);
            else _mqHit.Remove(clip);
        }

        var desired = new HashSet<GameClip>(_mqBase);
        if (_mqCtrl) desired.SymmetricExceptWith(_mqHit);
        else desired.UnionWith(_mqHit);

        bool changed = false;
        foreach (var clip in _selection)
        {
            if (desired.Contains(clip)) continue;
            clip.IsSelected = false;
            changed = true;
        }
        if (changed) _selection.RemoveAll(c => !c.IsSelected);
        foreach (var clip in desired)
        {
            if (clip.IsSelected) continue;
            AddToSelection(clip);
            changed = true;
        }
        if (changed) UpdateSelectionBar();
    }
}
