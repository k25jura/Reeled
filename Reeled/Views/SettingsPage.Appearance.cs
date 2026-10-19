using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Reeled.Helpers;
using Reeled.Services;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

namespace Reeled.Views;

/// <summary>Accent colour swatches and the interface-size stepper.</summary>
public sealed partial class SettingsPage
{
    private const int MaxRecentAccents = 4;

    private sealed class AccentSwatch
    {
        public required string Value;
        public required Ellipse Ring;
        public required FontIcon Check;
        public required ScaleTransform CheckScale;
        public FontIcon? Mark;
        public bool IsSelected;
        public bool IsHovered;
    }

    private readonly List<AccentSwatch> _accentSwatches = new();
    private string? _accentBeforePicker;
    private bool _accentPickerCommitted;
    private DispatcherTimer? _accentPreviewTimer;
    private Color _pendingPreviewColor;

    private static ILocalStorageService Storage => App.GetService<ILocalStorageService>();

    private void BuildAccentSwatches()
    {
        if (AccentSwatchesPanel == null) return;
        var loc = ViewModel.Loc;
        AccentSwatchesPanel.Children.Clear();
        _accentSwatches.Clear();

        AddAccentSwatch(Accent.SystemMode, Accent.SystemColor, loc["Appearance_AccentSystem"], "");
        AddSwatchDivider();

        foreach (var (hex, nameKey) in Accent.Presets)
        {
            if (Accent.TryParse(hex, out var color)) AddAccentSwatch(hex, color, loc[nameKey], null);
        }

        var recents = Storage.CurrentSettings.RecentAccentColors
            .Where(h => Accent.TryParse(h, out _) && !IsPreset(h))
            .Take(MaxRecentAccents)
            .ToList();
        if (recents.Count > 0)
        {
            AddSwatchDivider();
            foreach (var hex in recents)
            {
                Accent.TryParse(hex, out var color);
                AddAccentSwatch(hex, color, $"{loc["Appearance_AccentCustom"]} · {hex}", null);
            }
        }

        AddSwatchDivider();
        AccentSwatchesPanel.Children.Add(CreateCustomAccentButton(loc));
        UpdateAccentSelection();
    }

    private static bool IsPreset(string hex) =>
        Accent.Presets.Any(p => string.Equals(p.Hex, hex, StringComparison.OrdinalIgnoreCase));

    private void AddSwatchDivider()
    {
        AccentSwatchesPanel.Children.Add(new Border
        {
            Width = 1,
            Height = 20,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
        });
    }

    private void AddAccentSwatch(string value, Color color, string name, string? glyph)
    {
        var ring = new Ellipse
        {
            Width = 32,
            Height = 32,
            StrokeThickness = 2,
            Stroke = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            Opacity = 0
        };
        var fill = new Ellipse
        {
            Width = 24,
            Height = 24,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            StrokeThickness = 1
        };
        bool darkInk = Accent.Luminance(color) > 0.62;
        var ink = new SolidColorBrush(darkInk ? Color.FromArgb(230, 0, 0, 0) : Colors.White);
        var checkScale = new ScaleTransform { ScaleX = 0.4, ScaleY = 0.4, CenterX = 6, CenterY = 6 };
        var check = new FontIcon
        {
            Glyph = "",
            FontSize = 12,
            Width = 12,
            Height = 12,
            Foreground = ink,
            Opacity = 0,
            RenderTransform = checkScale,
            IsHitTestVisible = false
        };

        var content = new Grid { Width = 32, Height = 32 };
        content.Children.Add(ring);
        content.Children.Add(fill);
        FontIcon? mark = null;
        if (glyph != null)
        {
            // The Windows swatch carries a small mark so it reads as "follow Windows", not a colour
            content.Children.Add(mark = new FontIcon
            {
                Glyph = glyph,
                FontSize = 10,
                Foreground = ink,
                Opacity = 0.9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });
        }
        content.Children.Add(check);

        var button = CreateSwatchButton(content, name);
        var swatch = new AccentSwatch { Value = value, Ring = ring, Check = check, CheckScale = checkScale, Mark = mark };
        button.Click += (s, e) => SelectAccent(value);
        button.PointerEntered += (s, e) => { swatch.IsHovered = true; AnimateSwatch(swatch); };
        button.PointerExited += (s, e) => { swatch.IsHovered = false; AnimateSwatch(swatch); };
        _accentSwatches.Add(swatch);
        AccentSwatchesPanel.Children.Add(button);
    }

    private static Button CreateSwatchButton(UIElement content, string name)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                "<ContentPresenter Background='Transparent' HorizontalAlignment='Center' VerticalAlignment='Center' /></ControlTemplate>")
        };
        ToolTipService.SetToolTip(button, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    private Button CreateCustomAccentButton(ILocalizationService loc)
    {
        var rainbow = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        string[] stops = { "#FF5252", "#FFB300", "#66BB6A", "#29B6F6", "#7E57C2", "#EC407A" };
        for (int i = 0; i < stops.Length; i++)
        {
            Accent.TryParse(stops[i], out var c);
            rainbow.GradientStops.Add(new GradientStop { Color = c, Offset = i / (double)(stops.Length - 1) });
        }

        var ring = new Ellipse { Width = 32, Height = 32, StrokeThickness = 2, Stroke = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"], Opacity = 0 };
        var content = new Grid { Width = 32, Height = 32 };
        content.Children.Add(ring);
        content.Children.Add(new Ellipse { Width = 24, Height = 24, Fill = rainbow });
        content.Children.Add(new FontIcon { Glyph = "", FontSize = 11, Foreground = new SolidColorBrush(Colors.White), IsHitTestVisible = false });

        var button = CreateSwatchButton(content, loc["Appearance_AccentPick"]);
        button.PointerEntered += (s, e) => Fade(ring, 0.35);
        button.PointerExited += (s, e) => Fade(ring, 0);
        button.Click += (s, e) => OpenAccentPicker(button);
        return button;
    }

    private void OpenAccentPicker(FrameworkElement anchor)
    {
        var loc = ViewModel.Loc;
        _accentBeforePicker = Storage.CurrentSettings.AccentColor;
        _accentPickerCommitted = false;

        var picker = new ColorPicker
        {
            Color = Accent.Current,
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true,
            IsColorSliderVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Box,
            Width = 300
        };
        picker.ColorChanged += (s, e) => QueueAccentPreview(e.NewColor);

        var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };
        var apply = new Button
        {
            Content = loc["Appearance_AccentApply"],
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var cancel = new Button { Content = loc["Common_Cancel"], HorizontalAlignment = HorizontalAlignment.Stretch };
        apply.Click += (s, e) =>
        {
            _accentPickerCommitted = true;
            string hex = Accent.ToHex(picker.Color);
            RememberRecentAccent(hex);
            SelectAccent(hex);
            BuildAccentSwatches();
            flyout.Hide();
        };
        cancel.Click += (s, e) => flyout.Hide();

        var buttons = new Grid { ColumnSpacing = 8 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(apply, 1);
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);

        var panel = new StackPanel { Spacing = 14, RequestedTheme = ActualTheme };
        panel.Children.Add(new TextBlock { Text = loc["Appearance_AccentPick"], Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        panel.Children.Add(picker);
        panel.Children.Add(buttons);
        flyout.Content = panel;
        flyout.Closed += (s, e) =>
        {
            _accentPreviewTimer?.Stop();
            // Dismissed without applying: put the previous colour back
            if (!_accentPickerCommitted) Accent.Apply(_accentBeforePicker);
        };
        flyout.ShowAt(anchor);
    }

    /// <summary>Live preview while dragging in the picker, at most every ~60 ms.</summary>
    private void QueueAccentPreview(Color color)
    {
        _pendingPreviewColor = color;
        if (_accentPreviewTimer == null)
        {
            _accentPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _accentPreviewTimer.Tick += (s, e) =>
            {
                _accentPreviewTimer.Stop();
                Accent.Apply(Accent.ToHex(_pendingPreviewColor));
            };
        }
        if (!_accentPreviewTimer.IsEnabled) _accentPreviewTimer.Start();
    }

    private static void RememberRecentAccent(string hex)
    {
        if (IsPreset(hex)) return;
        var list = Storage.CurrentSettings.RecentAccentColors;
        list.RemoveAll(h => string.Equals(h, hex, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, hex);
        if (list.Count > MaxRecentAccents) list.RemoveRange(MaxRecentAccents, list.Count - MaxRecentAccents);
    }

    private void SelectAccent(string value)
    {
        var settings = Storage.CurrentSettings;
        settings.AccentColor = value;
        _ = Storage.SaveSettingsAsync(settings);
        Accent.Apply(value);
    }

    private void UpdateAccentSelection()
    {
        if (_accentSwatches.Count == 0) return;
        string current = Storage.CurrentSettings.AccentColor ?? Accent.SystemMode;
        if (!Accent.TryParse(current, out _)) current = Accent.SystemMode;

        foreach (var swatch in _accentSwatches)
        {
            bool selected = string.Equals(swatch.Value, current, StringComparison.OrdinalIgnoreCase);
            if (selected == swatch.IsSelected && swatch.Check.Opacity == (selected ? 1 : 0)) continue;
            swatch.IsSelected = selected;
            AnimateSwatch(swatch);
        }

        if (AccentCurrentNameText != null)
        {
            var loc = ViewModel.Loc;
            var preset = Accent.Presets.FirstOrDefault(p => string.Equals(p.Hex, current, StringComparison.OrdinalIgnoreCase));
            AccentCurrentNameText.Text = current == Accent.SystemMode
                ? loc["Appearance_AccentSystem"]
                : preset.Hex != null ? loc[preset.NameKey] : current.ToUpperInvariant();
        }
    }

    private static void AnimateSwatch(AccentSwatch swatch)
    {
        Fade(swatch.Ring, swatch.IsSelected ? 1 : swatch.IsHovered ? 0.35 : 0);
        var sb = new Storyboard();
        sb.Children.Add(Motion.Spline(swatch.Check, "Opacity", swatch.IsSelected ? 1 : 0, Motion.Short));
        sb.Children.Add(Motion.Spline(swatch.CheckScale, "ScaleX", swatch.IsSelected ? 1 : 0.4, Motion.Medium));
        sb.Children.Add(Motion.Spline(swatch.CheckScale, "ScaleY", swatch.IsSelected ? 1 : 0.4, Motion.Medium));
        if (swatch.Mark != null) sb.Children.Add(Motion.Spline(swatch.Mark, "Opacity", swatch.IsSelected ? 0 : 0.9, Motion.Short));
        Motion.BeginExact(sb);
    }

    private static void Fade(UIElement element, double to)
    {
        var sb = new Storyboard();
        sb.Children.Add(Motion.Spline(element, "Opacity", to, Motion.Micro + 60));
        Motion.BeginExact(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // Interface size
    // ---------------------------------------------------------------------------------------------

    private void UpdateZoomControls()
    {
        if (ZoomValueText == null) return;
        ZoomValueText.Text = UiZoom.Format(UiZoom.Current);
        ZoomInButton.IsEnabled = UiZoom.CanZoomIn;
        ZoomOutButton.IsEnabled = UiZoom.CanZoomOut;
        bool isDefault = Math.Abs(UiZoom.Current - 1) < 0.001;
        ZoomResetButton.IsEnabled = !isDefault;
        ZoomResetButton.Opacity = isDefault ? 0 : 1;
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => UiZoom.Step(+1);
    private void OnZoomOutClick(object sender, RoutedEventArgs e) => UiZoom.Step(-1);
    private void OnZoomResetClick(object sender, RoutedEventArgs e) => UiZoom.Set(1.0);
}
