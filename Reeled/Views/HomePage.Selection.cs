using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Reeled.Views;

/// <summary>
/// Selecting clips like in File Explorer: Ctrl+click toggles, Shift+click selects a range, Ctrl+A selects
/// all, Esc or a click on empty space clears. A plain click still plays a clip; once something is selected
/// it toggles instead (so nothing starts playing by accident while picking), and a small bar at the bottom
/// shows the count with the usual actions.
/// </summary>
public sealed partial class HomePage
{
    private readonly List<GameClip> _selection = new();
    private GameClip? _selectionAnchor;
    private Border? _selectionBar;
    private TextBlock? _selectionText;
    private StackPanel _fileOpStack = null!;

    private bool IsSelecting => _selection.Count > 0;

    private static bool IsShiftDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>Called once from the constructor.</summary>
    private void WireFileOps()
    {
        _fileOpStack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        FileOpHost.Children.Add(_fileOpStack);

        // A different view shows other clips: the selection doesn't carry over
        ViewModel.ViewContentReplaced += () =>
        {
            ClearSelection();
            ReapplyCutMarks();
        };
        ViewModel.FilteredClips.CollectionChanged += (s, e) =>
        {
            if (IsSelecting) PruneSelection();
        };
        LayoutLoc.LanguageChanged += (s, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_selectionBar != null) UpdateSelectionBar();
        });
        WireClipboard();
    }

    /// <summary>The clips in the order they are shown (the groups follow each other when grouped by date).</summary>
    private IEnumerable<GameClip> DisplayOrder() =>
        ViewModel.DateGrouping == DateGroupingMode.None
            ? ViewModel.FilteredClips
            : ViewModel.GroupedClips.SelectMany(g => g);

    private void ClearSelection()
    {
        if (_selection.Count == 0)
        {
            _selectionAnchor = null;
            return;
        }
        foreach (var clip in _selection) clip.IsSelected = false;
        _selection.Clear();
        _selectionAnchor = null;
        UpdateSelectionBar();
    }

    private void AddToSelection(GameClip clip)
    {
        if (clip.IsSelected) return;
        clip.IsSelected = true;
        _selection.Add(clip);
    }

    private void SelectOnly(GameClip clip)
    {
        foreach (var other in _selection.Where(c => !ReferenceEquals(c, clip))) other.IsSelected = false;
        _selection.RemoveAll(c => !ReferenceEquals(c, clip));
        AddToSelection(clip);
        _selectionAnchor = clip;
        UpdateSelectionBar();
    }

    private void ToggleSelection(GameClip clip)
    {
        if (clip.IsSelected)
        {
            clip.IsSelected = false;
            _selection.Remove(clip);
        }
        else
        {
            AddToSelection(clip);
        }
        _selectionAnchor = clip;
        UpdateSelectionBar();
    }

    /// <summary>Shift+click: everything between the anchor and the clicked clip (Ctrl keeps what was selected).</summary>
    private void SelectRangeTo(GameClip clip, bool keepExisting)
    {
        var order = DisplayOrder().ToList();
        int to = order.IndexOf(clip);
        int from = _selectionAnchor != null ? order.IndexOf(_selectionAnchor) : -1;
        if (to < 0) return;
        if (from < 0)
        {
            SelectOnly(clip);
            return;
        }

        var anchor = _selectionAnchor;
        if (!keepExisting)
        {
            foreach (var other in _selection) other.IsSelected = false;
            _selection.Clear();
        }
        for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) AddToSelection(order[i]);
        _selectionAnchor = anchor;
        UpdateSelectionBar();
    }

    private void SelectAllClips()
    {
        foreach (var clip in DisplayOrder()) AddToSelection(clip);
        UpdateSelectionBar();
    }

    /// <summary>Clips that were filtered out or removed can't stay selected.</summary>
    private void PruneSelection()
    {
        var visible = new HashSet<GameClip>(ViewModel.FilteredClips);
        bool changed = false;
        for (int i = _selection.Count - 1; i >= 0; i--)
        {
            if (visible.Contains(_selection[i])) continue;
            _selection[i].IsSelected = false;
            _selection.RemoveAt(i);
            changed = true;
        }
        if (_selectionAnchor != null && !visible.Contains(_selectionAnchor)) _selectionAnchor = null;
        if (changed) UpdateSelectionBar();
    }

    /// <summary>A click on a card or row; true when it was a selection gesture rather than "play".</summary>
    private bool HandleSelectionClick(GameClip clip)
    {
        if (IsShiftDown())
        {
            SelectRangeTo(clip, keepExisting: IsCtrlDown());
            return true;
        }
        if (IsCtrlDown() || IsSelecting)
        {
            ToggleSelection(clip);
            return true;
        }
        return false;
    }

    /// <summary>
    /// A press on empty space between and around the clips: clears the selection (Ctrl keeps it) and gets ready
    /// for a rubber-band drag (HomePage.Marquee.cs).
    /// </summary>
    private void OnClipsAreaPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsShiftDown()) return;
        if (!e.GetCurrentPoint(ClipsHost).Properties.IsLeftButtonPressed) return;
        if (e.OriginalSource is DependencyObject source &&
            (FindParent<GridViewItem>(source) != null || FindParent<ListViewItem>(source) != null ||
             FindParent<Microsoft.UI.Xaml.Controls.Primitives.ScrollBar>(source) != null || FindParent<Button>(source) != null))
        {
            return;
        }
        bool ctrl = IsCtrlDown();
        if (!ctrl && IsSelecting) ClearSelection();
        BeginMarqueeCandidate(e, ctrl);
    }

    /// <summary>The clips an action (copy, cut, delete) applies to: the selection, else the card under the pointer.</summary>
    private IReadOnlyList<GameClip> ActionClips()
    {
        if (IsSelecting) return _selection.ToList();
        return _hoveredClip != null ? new[] { _hoveredClip } : Array.Empty<GameClip>();
    }

    /// <summary>The right-clicked clip, or all selected clips when it is one of them.</summary>
    private IReadOnlyList<GameClip> ContextTargets(GameClip clip) =>
        clip.IsSelected && _selection.Count > 1 ? _selection.ToList() : new[] { clip };

    // ---------------------------------------------------------------------------------------------
    // Selection bar
    // ---------------------------------------------------------------------------------------------

    private void UpdateSelectionBar()
    {
        if (_fileOpStack == null) return;
        if (_selection.Count == 0)
        {
            if (_selectionBar != null) HideFloating(_selectionBar);
            return;
        }

        _selectionBar ??= BuildSelectionBar();
        _selectionText!.Text = string.Format(LayoutLoc["Selection_Count"], _selection.Count);
        ShowFloating(_selectionBar);
    }

    private Border BuildSelectionBar()
    {
        var loc = LayoutLoc;
        _selectionText = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 10, 0)
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(_selectionText);
        row.Children.Add(BarButton("", () => loc["ContextMenu_Copy"], () => _ = CopyClipsToClipboardAsync(_selection.ToList(), cut: false)));
        row.Children.Add(BarButton("", () => loc["ContextMenu_Cut"], () => _ = CopyClipsToClipboardAsync(_selection.ToList(), cut: true)));
        row.Children.Add(BarButton("", () => loc["ContextMenu_DeleteRecycle"], () => _ = ShowDeleteConfirmDialogAsync(_selection.ToList())));
        row.Children.Add(new Border { Width = 1, Height = 18, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Background = Helpers.ThemeBrushes.Get("DividerStrokeColorDefaultBrush", this) });
        row.Children.Add(BarButton("", () => loc["Selection_Clear"], ClearSelection));

        var bar = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };
        Helpers.ThemeBrushes.Track(bar, () =>
        {
            bar.Background = Helpers.ThemeBrushes.Get("SolidBackgroundFillColorSecondaryBrush", bar);
            bar.BorderBrush = Helpers.ThemeBrushes.Get("CardStrokeColorDefaultBrush", bar);
        });
        _fileOpStack.Children.Add(bar);
        return bar;
    }

    private Button BarButton(string glyph, Func<string> tooltip, Action click)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(9, 7, 9, 7),
            Content = new FontIcon { Glyph = glyph, FontSize = 14 }
        };
        // The tooltip is read when it opens so a language change needs no rebuild
        button.PointerEntered += (s, e) => ToolTipService.SetToolTip(button, tooltip());
        ToolTipService.SetToolTip(button, tooltip());
        button.Click += (s, e) => click();
        return button;
    }

    // ---------------------------------------------------------------------------------------------
    // Floating elements (selection bar, operation card): fade and rise in, fade out
    // ---------------------------------------------------------------------------------------------

    private readonly Dictionary<FrameworkElement, (Storyboard Story, bool Showing)> _floating = new();

    private void ShowFloating(FrameworkElement element, bool rise = true)
    {
        var translate = FloatingTranslate(element);
        double fromOpacity = element.Opacity;
        double fromY = translate.Y;
        if (_floating.TryGetValue(element, out var running))
        {
            if (running.Showing) return;
            running.Story.Stop();
        }
        else if (element.Visibility == Visibility.Visible && element.Opacity >= 1)
        {
            return;
        }

        element.Visibility = Visibility.Visible;
        if (Helpers.Motion.IsReduced)
        {
            element.Opacity = 1;
            translate.Y = 0;
            _floating.Remove(element);
            return;
        }

        var story = new Storyboard();
        story.Children.Add(Helpers.Motion.Spline(element, "Opacity", 1, Helpers.Motion.Medium, from: fromOpacity));
        story.Children.Add(Helpers.Motion.Spline(translate, "Y", 0, Helpers.Motion.Medium, from: !rise ? 0 : fromOpacity <= 0 ? 12 : fromY));
        story.Completed += (s, e) =>
        {
            if (!_floating.TryGetValue(element, out var current) || !ReferenceEquals(current.Story, story)) return;
            _floating.Remove(element);
            story.Stop();
            element.Opacity = 1;
            translate.Y = 0;
        };
        _floating[element] = (story, true);
        Helpers.Motion.BeginExact(story);
    }

    private void HideFloating(FrameworkElement element, Action? completed = null, bool rise = true)
    {
        var translate = FloatingTranslate(element);
        double fromOpacity = element.Opacity;
        double fromY = translate.Y;
        if (_floating.TryGetValue(element, out var running))
        {
            if (!running.Showing) return;
            running.Story.Stop();
        }
        else if (element.Visibility == Visibility.Collapsed)
        {
            completed?.Invoke();
            return;
        }

        if (Helpers.Motion.IsReduced)
        {
            element.Opacity = 0;
            element.Visibility = Visibility.Collapsed;
            _floating.Remove(element);
            completed?.Invoke();
            return;
        }

        var story = new Storyboard();
        story.Children.Add(Helpers.Motion.Spline(element, "Opacity", 0, Helpers.Motion.Short, from: fromOpacity, accelerate: true));
        story.Children.Add(Helpers.Motion.Spline(translate, "Y", rise ? 8 : 0, Helpers.Motion.Short, from: fromY, accelerate: true));
        story.Completed += (s, e) =>
        {
            if (!_floating.TryGetValue(element, out var current) || !ReferenceEquals(current.Story, story)) return;
            _floating.Remove(element);
            story.Stop();
            element.Opacity = 0;
            translate.Y = 0;
            element.Visibility = Visibility.Collapsed;
            completed?.Invoke();
        };
        _floating[element] = (story, false);
        Helpers.Motion.BeginExact(story);
    }

    private static TranslateTransform FloatingTranslate(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform existing) return existing;
        var translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }
}
