using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Reeled.Helpers;

namespace Reeled.Views;

/// <summary>
/// Motion of the left navigation when a category's sub-list opens and closes. The old approach animated
/// the lists' heights, which re-ran layout every frame (and, as the rail is centred, nudged every button
/// by half of each step). Here the layout changes exactly once, and the buttons glide from where they were
/// to where they now are with render transforms, stepped on the shared decelerate curve:
/// the closing list fades out, the layout switches, the buttons glide, then the new list fades in.
/// </summary>
public sealed partial class SettingsPage
{
    private sealed class NavTween
    {
        public required Action<double> Apply;
        public required long Start;
        public required double Duration;
        public double Delay;
        public bool Accelerate;
        public Action? Done;
    }

    private const double NavFadeOutMs = 110;
    private const double NavListDelayMs = 120;

    private string? _shownSubSection;
    private readonly List<NavTween> _navTweens = new();
    private readonly Dictionary<Button, TranslateTransform> _navShift = new();
    private bool _navLoopRunning;

    private TranslateTransform ShiftOf(Button button)
    {
        if (!_navShift.TryGetValue(button, out var shift))
        {
            shift = new TranslateTransform();
            button.RenderTransform = shift;
            _navShift[button] = shift;
        }
        return shift;
    }

    private StackPanel? ShownSubPanel() =>
        _shownSubSection != null && _subNavPanels.TryGetValue(_shownSubSection, out var panel) ? panel : null;

    private void AddNavTween(Action<double> apply, double duration, double delay = 0, bool accelerate = false, Action? done = null)
    {
        _navTweens.Add(new NavTween
        {
            Apply = apply,
            Start = Stopwatch.GetTimestamp(),
            Duration = duration,
            Delay = delay,
            Accelerate = accelerate,
            Done = done
        });
        if (_navLoopRunning) return;
        _navLoopRunning = true;
        CompositionTarget.Rendering += OnNavFrame;
    }

    private void OnNavFrame(object? sender, object e)
    {
        List<NavTween>? finished = null;
        foreach (var tween in _navTweens)
        {
            double ms = Stopwatch.GetElapsedTime(tween.Start).TotalMilliseconds - tween.Delay;
            if (ms < 0) continue;
            double t = Math.Min(1, ms / tween.Duration);
            tween.Apply(tween.Accelerate ? Motion.AccelerateAt(t) : Motion.DecelerateAt(t));
            if (t >= 1) (finished ??= new()).Add(tween);
        }

        if (finished != null)
        {
            foreach (var tween in finished) _navTweens.Remove(tween);
            foreach (var tween in finished) tween.Done?.Invoke();
        }

        if (_navTweens.Count == 0)
        {
            _navLoopRunning = false;
            CompositionTarget.Rendering -= OnNavFrame;
        }
    }

    /// <summary>Opens the active category's sub-list and closes the previous one.</summary>
    private void UpdateSubNavigation(string sectionTag, bool animate)
    {
        if (_expandedSubSection == sectionTag) return;
        _expandedSubSection = sectionTag;

        // Start on the section's first group until the scroll position says otherwise
        foreach (var (_, _, header, section) in _subNavItems)
        {
            if (section == sectionTag) { SetActiveSubHeader(header.Name); break; }
        }

        _subNavPanels.TryGetValue(sectionTag, out var next);
        string? nextTag = next != null ? sectionTag : null;

        if (!animate || Motion.IsReduced)
        {
            SwapSubNav(next, nextTag, flip: false);
            DispatcherQueue.TryEnqueue(RealignNavIndicator);
            return;
        }

        // A list that is still in layout (also one that is half faded) is what the new motion starts from
        var shown = ShownSubPanel();
        _navTweens.Clear();

        if (shown == next)
        {
            if (next != null && next.Opacity < 1)
            {
                double from = next.Opacity;
                AddNavTween(k => next.Opacity = from + (1 - from) * k, Motion.Short);
            }
            SettleNavShifts();
            return;
        }

        if (shown != null)
        {
            // The old items leave first, then the gap closes and the new list opens in a single layout pass
            // A list that hasn't faded in yet (or is mostly gone) doesn't need the full wait: retarget at once
            double from = shown.Opacity;
            if (from < 0.05)
            {
                SwapSubNav(next, nextTag, flip: true);
                return;
            }
            AddNavTween(k => shown.Opacity = from * (1 - k), Math.Max(40, NavFadeOutMs * from), accelerate: true, done: () => SwapSubNav(next, nextTag, flip: true));
            SettleNavShifts();
        }
        else
        {
            SwapSubNav(next, nextTag, flip: true);
        }
    }

    /// <summary>Lets buttons that were mid-glide finish their move.</summary>
    private void SettleNavShifts()
    {
        foreach (var shift in _navShift.Values)
        {
            if (Math.Abs(shift.Y) < 0.1) { shift.Y = 0; continue; }
            var target = shift;
            double from = shift.Y;
            AddNavTween(k => target.Y = from * (1 - k), Motion.Medium);
        }
    }

    private void SwapSubNav(StackPanel? next, string? nextTag, bool flip)
    {
        _navTweens.Clear();
        var old = ShownSubPanel();
        var buttons = CategoriesStackPanel.Children.OfType<Button>().ToList();

        // Where each button is now, wherever its glide had got to
        double[]? before = flip ? MeasureNavButtons(buttons, keepShift: true) : null;
        if (!flip)
        {
            foreach (var shift in _navShift.Values) shift.Y = 0;
        }

        if (old != null && old != next)
        {
            old.Visibility = Visibility.Collapsed;
            old.Opacity = 0;
        }
        if (next != null && next != old)
        {
            next.Visibility = Visibility.Visible;
            next.Opacity = flip ? 0 : 1;
        }
        _shownSubSection = nextTag;
        if (!flip || before == null) return;

        UpdateLayout();
        double[] after = MeasureNavButtons(buttons, keepShift: false);
        for (int i = 0; i < buttons.Count; i++)
        {
            double delta = before[i] - after[i];
            if (Math.Abs(delta) < 0.5) continue;
            var shift = ShiftOf(buttons[i]);
            shift.Y = delta;
            AddNavTween(k => shift.Y = delta * (1 - k), Motion.Long);
        }

        if (next != null && next != old && next.RenderTransform is TranslateTransform rise)
        {
            // The items arrive once the buttons have cleared their space
            rise.Y = -6;
            AddNavTween(k => { next.Opacity = k; rise.Y = -6 * (1 - k); }, Motion.Medium, NavListDelayMs);
        }

        RealignNavIndicator();
    }

    /// <summary>Y of every category button in the page. Shifts are zeroed first so the layout position is read cleanly.</summary>
    private double[] MeasureNavButtons(List<Button> buttons, bool keepShift)
    {
        var ys = new double[buttons.Count];
        for (int i = 0; i < buttons.Count; i++)
        {
            var shift = ShiftOf(buttons[i]);
            double current = shift.Y;
            shift.Y = 0;
            try
            {
                ys[i] = buttons[i].TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            }
            catch
            {
                ys[i] = 0;
            }
            if (keepShift) ys[i] += current;
        }
        return ys;
    }

    private void RealignNavIndicator()
    {
        if (FindCategoryButton(_activeSectionTag) is Button active) AnimateIndicatorTo(active, animate: true);
    }
}
