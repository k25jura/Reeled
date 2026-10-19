using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Reeled.Controls;

/// <summary>
/// Equal-width tabs (icon + label) with an accent underline that slides to the selected tab. Used at the
/// top of the player's panels (tracks, moments, queue, session settings).
/// </summary>
public sealed class TabStrip : Grid
{
    private readonly List<(Button Button, FontIcon Icon, TextBlock Label)> _tabs = new();
    private readonly Border _indicator = new()
    {
        Height = 2,
        CornerRadius = new CornerRadius(1),
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Left,
        IsHitTestVisible = false
    };
    private readonly TranslateTransform _indicatorTranslate = new();
    private readonly ScaleTransform _indicatorScale = new() { ScaleX = 1 };
    private readonly Border _baseline = new() { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
    private int _selectedIndex = -1;
    private bool _compact;
    private const double SelectedWeight = 2.4;

    public event EventHandler<int>? SelectionChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => Select(value, raise: false, animate: IsLoaded);
    }

    public int Count => _tabs.Count;

    public TabStrip()
    {
        Height = 40;
        _indicator.RenderTransform = new TransformGroup { Children = { _indicatorScale, _indicatorTranslate } };
        // Brushes follow the theme of the surface the strip sits on (the player is always dark)
        Helpers.ThemeBrushes.Bind(_indicator, Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        _baseline.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(22, 255, 255, 255));
        Helpers.ThemeBrushes.Track(this, UpdateTabBrushes);
        Children.Add(_baseline);
        Children.Add(_indicator);
        SizeChanged += (s, e) =>
        {
            UpdateCompact();
            PlaceIndicator(animate: false);
        };
    }

    public void SetTabs(IEnumerable<(string Glyph, string Text)> tabs)
    {
        foreach (var t in _tabs) Children.Remove(t.Button);
        _tabs.Clear();
        ColumnDefinitions.Clear();

        int i = 0;
        foreach (var (glyph, text) in tabs)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new FontIcon { Glyph = glyph, FontSize = 13 };
            var label = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(icon);
            panel.Children.Add(label);
            var button = new Button
            {
                Content = panel,
                Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(6, 0, 6, 2),
                Margin = new Thickness(2, 3, 2, 4),
                CornerRadius = new CornerRadius(5)
            };
            int index = i;
            button.Click += (s, e) => Select(index, raise: true, animate: true);
            SetColumn(button, i);
            Children.Insert(Children.Count - 2, button);
            _tabs.Add((button, icon, label));
            i++;
        }
        SetColumnSpan(_indicator, Math.Max(1, _tabs.Count));
        SetColumnSpan(_baseline, Math.Max(1, _tabs.Count));
        int keep = Math.Clamp(_selectedIndex, 0, Math.Max(0, _tabs.Count - 1));
        _selectedIndex = -1;
        _compact = false;
        Select(keep, raise: false, animate: false);
        UpdateCompact();
    }

    public void SetText(int index, string text)
    {
        if (index >= 0 && index < _tabs.Count) _tabs[index].Label.Text = text;
    }

    private void Select(int index, bool raise, bool animate)
    {
        if (_tabs.Count == 0) { _selectedIndex = index; return; }
        index = Math.Clamp(index, 0, _tabs.Count - 1);
        bool changed = index != _selectedIndex;
        _selectedIndex = index;

        UpdateTabBrushes();
        ApplyCompactLayout();
        PlaceIndicator(animate);
        if (changed && raise) SelectionChanged?.Invoke(this, index);
    }

    private void UpdateTabBrushes()
    {
        if (_selectedIndex < 0) return;
        var primary = Helpers.ThemeBrushes.Get("TextFillColorPrimaryBrush", this);
        var secondary = Helpers.ThemeBrushes.Get("TextFillColorSecondaryBrush", this);
        var accentText = Helpers.ThemeBrushes.Get("AccentTextFillColorPrimaryBrush", this);
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool selected = i == _selectedIndex;
            _tabs[i].Label.Foreground = selected ? primary : secondary;
            _tabs[i].Icon.Foreground = selected ? accentText : secondary;
            _tabs[i].Label.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    /// <summary>
    /// When the labels don't all fit, only the selected tab shows its label (and gets more room);
    /// the others fold to icons with tooltips.
    /// </summary>
    private void UpdateCompact()
    {
        if (_tabs.Count == 0 || ActualWidth <= 0) return;
        double needed = 0;
        foreach (var (button, icon, label) in _tabs)
        {
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            needed += label.DesiredSize.Width + 13 + 8 + 12 + 8;
        }
        bool compact = needed > ActualWidth;
        if (compact == _compact) return;
        _compact = compact;
        ApplyCompactLayout();
    }

    private double WeightOf(int i) => _compact && i == _selectedIndex ? SelectedWeight : 1.0;

    private void ApplyCompactLayout()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool showLabel = !_compact || i == _selectedIndex;
            _tabs[i].Label.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(_tabs[i].Button, showLabel ? null : _tabs[i].Label.Text);
            if (i < ColumnDefinitions.Count) ColumnDefinitions[i].Width = new GridLength(WeightOf(i), GridUnitType.Star);
        }
    }

    private void PlaceIndicator(bool animate)
    {
        if (_tabs.Count == 0 || ActualWidth <= 0 || _selectedIndex < 0) return;
        double total = 0;
        for (int i = 0; i < _tabs.Count; i++) total += WeightOf(i);
        double before = 0;
        for (int i = 0; i < _selectedIndex; i++) before += WeightOf(i);
        double tabWidth = ActualWidth * WeightOf(_selectedIndex) / total;
        double width = Math.Min(tabWidth - 24, 56);
        _indicator.Width = Math.Max(16, width);
        double x = ActualWidth * before / total + (tabWidth - _indicator.Width) / 2;

        if (!animate || Helpers.Motion.IsReduced)
        {
            _indicatorTranslate.X = x;
            return;
        }
        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(_indicatorTranslate, "X", x, Helpers.Motion.Medium));
        Helpers.Motion.BeginExact(sb);
    }

    /// <summary>Swaps panel content with a short fade and slide in the direction of travel.</summary>
    public static void SwitchContent(IList<FrameworkElement> panels, int from, int to)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            if (i != to) panels[i].Visibility = Visibility.Collapsed;
        }
        if (to < 0 || to >= panels.Count) return;
        var target = panels[to];
        target.Visibility = Visibility.Visible;
        if (Helpers.Motion.IsReduced || from == to) return;

        if (target.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            target.RenderTransform = translate;
        }
        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(target, "Opacity", 1, Helpers.Motion.Medium, from: 0));
        sb.Children.Add(Helpers.Motion.Spline(translate, "X", 0, Helpers.Motion.Medium, from: to > from ? 14 : -14));
        Helpers.Motion.BeginExact(sb);
    }
}
