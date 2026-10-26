using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Reeled.Controls;

/// <summary>One row of a <see cref="SearchPicker"/>.</summary>
public sealed class PickerEntry
{
    public string Text { get; init; } = string.Empty;
    /// <summary>Where the row lives (e.g. its parent folders); shown under the name while searching.</summary>
    public string? Detail { get; init; }
    public string? Glyph { get; init; }
    public ImageSource? Image { get; init; }
    /// <summary>Right-aligned, dim (a clip count).</summary>
    public string? Trailing { get; init; }
    /// <summary>Null for a plain row; a value makes it a check row that keeps the picker open.</summary>
    public bool? IsChecked { get; set; }
    /// <summary>The row for what is shown now (the open folder).</summary>
    public bool IsCurrent { get; init; }
    /// <summary>Tree depth, for indentation; ignored while searching.</summary>
    public int Depth { get; init; }
    public bool IsHeader { get; init; }
    public bool IsEnabled { get; init; } = true;
    public Action<PickerEntry>? Invoked { get; init; }
    /// <summary>Extra wiring for the row's button: a context menu, a drop target.</summary>
    public Action<Button>? Prepare { get; init; }
}

/// <summary>
/// A dropdown for long lists (folders, tags): a search box over a scrolling list with icons, counts and
/// check marks, and fixed footer commands. Type to narrow it, arrows to move, Enter to pick. Looks like
/// the library's menus (same acrylic, row height and radius) so it reads as one of them.
/// </summary>
public sealed class SearchPicker
{
    private const double RowHeight = 34;
    private const double ListMaxHeight = 520;

    private readonly Flyout _flyout = new() { ShouldConstrainToRootBounds = true };
    private readonly TextBox _search = new();
    private readonly StackPanel _list = new() { Spacing = 1, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled };
    private readonly StackPanel _footer = new() { Spacing = 1, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty = new() { FontSize = 13, Margin = new Thickness(12, 10, 12, 12), Visibility = Visibility.Collapsed };
    private readonly List<(PickerEntry Entry, Button? Row, FrameworkElement View)> _rows = new();
    private readonly List<Button> _footerRows = new();
    private readonly Grid _root;

    public FlyoutBase Flyout => _flyout;

    public event EventHandler<object>? Closed
    {
        add => _flyout.Closed += value;
        remove => _flyout.Closed -= value;
    }

    public SearchPicker()
    {
        _search.Margin = new Thickness(4, 4, 4, 6);
        _search.TextChanged += (s, e) => ApplyFilter();
        _search.KeyDown += OnSearchKeyDown;

        _scroll = new ScrollViewer
        {
            Content = _list,
            MaxHeight = ListMaxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 0, 2)
        };

        _root = new Grid { MinWidth = 280, MaxWidth = 380 };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Children.Add(_search);
        Grid.SetRow(_scroll, 1);
        _root.Children.Add(_scroll);
        Grid.SetRow(_empty, 2);
        _root.Children.Add(_empty);
        Grid.SetRow(_footer, 3);
        _root.Children.Add(_footer);
        _flyout.Content = _root;

        _flyout.Opened += (s, e) =>
        {
            Helpers.CursorHelper.ShowArrowNow();
            _search.Focus(FocusState.Programmatic);
            // Start where the current row is, not at the top of a long list
            var current = _rows.FirstOrDefault(r => r.Entry.IsCurrent && r.Row != null).Row;
            current?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.4, AnimationDesired = false });
        };
    }

    /// <summary>The search box's placeholder and the line shown when nothing matches (current language).</summary>
    public void SetTexts(string searchPlaceholder, string noMatches)
    {
        _search.PlaceholderText = searchPlaceholder;
        _empty.Text = noMatches;
    }

    /// <summary>Theme and placement; the presenter wears the same acrylic as the library's menus.</summary>
    public void Configure(ElementTheme theme, FlyoutPlacementMode placement)
    {
        _flyout.Placement = placement;
        _root.RequestedTheme = theme;
        var tint = theme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(255, 249, 249, 249)
            : Windows.UI.Color.FromArgb(255, 32, 32, 32);
        var style = new Style(typeof(FlyoutPresenter));
        if (Application.Current.Resources.TryGetValue("DefaultFlyoutPresenterStyle", out var baseStyle) && baseStyle is Style s)
        {
            style.BasedOn = s;
        }
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Helpers.Backdrop.Create(tint, Helpers.Backdrop.Surface.Flyout)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 400.0));
        // The list scrolls itself; the presenter's own scroller would scroll the search box away
        style.Setters.Add(new Setter(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Disabled));
        style.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        _flyout.FlyoutPresenterStyle = style;
    }

    public void SetEntries(IEnumerable<PickerEntry> entries, IEnumerable<PickerEntry>? footer = null)
    {
        _rows.Clear();
        _list.Children.Clear();
        _footer.Children.Clear();
        _footerRows.Clear();
        _search.Text = string.Empty;

        foreach (var entry in entries)
        {
            var (row, view) = Build(entry);
            _rows.Add((entry, row, view));
            _list.Children.Add(view);
        }

        var footerEntries = footer?.ToList() ?? new List<PickerEntry>();
        if (footerEntries.Count > 0)
        {
            _footer.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(-4, 4, -4, 4),
                Background = Helpers.ThemeBrushes.Get("DividerStrokeColorDefaultBrush", _root.RequestedTheme)
            });
            foreach (var entry in footerEntries)
            {
                var (row, view) = Build(entry);
                if (row != null) _footerRows.Add(row);
                _footer.Children.Add(view);
            }
        }
        ApplyFilter();
    }

    /// <summary>Enables or disables a footer command while the picker is open (e.g. "Clear filters").</summary>
    public void SetFooterEnabled(int index, bool enabled)
    {
        if (index >= 0 && index < _footerRows.Count) _footerRows[index].IsEnabled = enabled;
    }

    public void ShowAt(FrameworkElement anchor) => _flyout.ShowAt(anchor);

    public void Hide() => _flyout.Hide();

    private (Button? Row, FrameworkElement View) Build(PickerEntry entry)
    {
        var theme = _root.RequestedTheme;
        if (entry.IsHeader)
        {
            var header = new TextBlock
            {
                Text = entry.Text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, 10, 12, 4),
                Foreground = Helpers.ThemeBrushes.Get("TextFillColorSecondaryBrush", theme)
            };
            return (null, header);
        }

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconHost = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(iconHost);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = entry.Text,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = entry.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal
        };
        var detail = new TextBlock
        {
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed,
            Foreground = Helpers.ThemeBrushes.Get("TextFillColorTertiaryBrush", theme)
        };
        text.Children.Add(name);
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (!string.IsNullOrEmpty(entry.Trailing))
        {
            var trailing = new TextBlock
            {
                Text = entry.Trailing,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Helpers.ThemeBrushes.Get("TextFillColorTertiaryBrush", theme)
            };
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        var button = new Button
        {
            Content = grid,
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = RowHeight,
            Padding = new Thickness(12 + entry.Depth * 16, 5, 12, 5),
            CornerRadius = new CornerRadius(4),
            IsEnabled = entry.IsEnabled,
            Tag = (name, detail, iconHost)
        };
        AutomationName(button, entry);
        PaintIcon(iconHost, entry, theme);
        button.Click += (s, e) => Invoke(entry, button);
        button.KeyDown += OnRowKeyDown;
        button.CharacterReceived += OnRowCharacter;
        entry.Prepare?.Invoke(button);
        return (button, button);
    }

    private static void AutomationName(Button button, PickerEntry entry) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,
            string.IsNullOrEmpty(entry.Trailing) ? entry.Text : $"{entry.Text}, {entry.Trailing}");

    /// <summary>A check mark for checked rows, otherwise the row's image or glyph (accent for the current row).</summary>
    private static void PaintIcon(Grid host, PickerEntry entry, ElementTheme theme)
    {
        host.Children.Clear();
        if (entry.IsChecked == true || (entry.IsCurrent && entry.Image == null && entry.Glyph == null))
        {
            host.Children.Add(new FontIcon
            {
                Glyph = "",
                FontSize = 14,
                Foreground = Helpers.ThemeBrushes.Get("AccentTextFillColorPrimaryBrush", theme)
            });
            return;
        }
        if (entry.Image != null)
        {
            host.Children.Add(new Grid
            {
                CornerRadius = new CornerRadius(4),
                Children = { new Image { Source = entry.Image, Stretch = Stretch.UniformToFill } }
            });
            return;
        }
        if (entry.Glyph != null)
        {
            host.Children.Add(new FontIcon
            {
                Glyph = entry.Glyph,
                FontSize = 14,
                Foreground = Helpers.ThemeBrushes.Get(entry.IsCurrent ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush", theme)
            });
        }
    }

    private void Invoke(PickerEntry entry, Button button)
    {
        if (entry.IsChecked is bool on)
        {
            entry.IsChecked = !on;
            if (button.Tag is ValueTuple<TextBlock, TextBlock, Grid> parts) PaintIcon(parts.Item3, entry, _root.RequestedTheme);
            entry.Invoked?.Invoke(entry);
            return;
        }
        _flyout.Hide();
        entry.Invoked?.Invoke(entry);
    }

    private void ApplyFilter()
    {
        string query = _search.Text.Trim();
        bool searching = query.Length > 0;
        bool Matches(PickerEntry e) =>
            e.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            (e.Detail?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);

        int visible = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            var (entry, row, view) = _rows[i];
            if (entry.IsHeader)
            {
                // A section heading stays while any row under it is shown
                bool any = false;
                for (int j = i + 1; j < _rows.Count && !_rows[j].Entry.IsHeader; j++)
                {
                    if (!searching || Matches(_rows[j].Entry)) { any = true; break; }
                }
                view.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }

            bool show = !searching || Matches(entry);
            view.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) visible++;
            if (row?.Tag is ValueTuple<TextBlock, TextBlock, Grid> parts)
            {
                // Search results are a flat list; the path says where each one is
                row.Padding = new Thickness(12 + (searching ? 0 : entry.Depth * 16), 5, 12, 5);
                bool showDetail = searching && !string.IsNullOrEmpty(entry.Detail);
                parts.Item2.Text = entry.Detail ?? string.Empty;
                parts.Item2.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        _empty.Visibility = searching && visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = visible == 0 && searching ? Visibility.Collapsed : Visibility.Visible;
        _empty.Foreground = Helpers.ThemeBrushes.Get("TextFillColorSecondaryBrush", _root.RequestedTheme);
        if (searching) _scroll.ChangeView(null, 0, null, true);
    }

    private IEnumerable<Button> VisibleRows() => _rows
        .Where(r => r.Row != null && r.View.Visibility == Visibility.Visible && r.Row.IsEnabled)
        .Select(r => r.Row!);

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            VisibleRows().FirstOrDefault()?.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter)
        {
            var first = _rows.FirstOrDefault(r => r.Row != null && r.View.Visibility == Visibility.Visible && r.Row.IsEnabled);
            if (first.Row != null) Invoke(first.Entry, first.Row);
            e.Handled = true;
        }
    }

    private void OnRowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Up from the first row goes back to the search box
        if (e.Key == VirtualKey.Up && ReferenceEquals(sender, VisibleRows().FirstOrDefault()))
        {
            _search.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }

    /// <summary>Typing on a row keeps searching (any layout, so Cyrillic too).</summary>
    private void OnRowCharacter(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (char.IsControl(e.Character) || char.IsWhiteSpace(e.Character) && _search.Text.Length == 0) return;
        _search.Text += e.Character;
        _search.Focus(FocusState.Keyboard);
        _search.SelectionStart = _search.Text.Length;
        e.Handled = true;
    }
}
