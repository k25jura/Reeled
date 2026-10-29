using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Reeled.Controls;
using Reeled.Helpers;
using Reeled.Services;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

namespace Reeled.Views;

/// <summary>Accent colour swatches and the interface-size stepper.</summary>
public sealed partial class SettingsPage
{
    private const int MaxRecentAccents = 4;

    private bool _accentChooserWired;

    private static ILocalStorageService Storage => App.GetService<ILocalStorageService>();

    private void BuildAccentSwatches()
    {
        if (AccentChooser == null) return;
        var loc = ViewModel.Loc;

        if (!_accentChooserWired)
        {
            _accentChooserWired = true;
            AccentChooser.MaxRecents = MaxRecentAccents;
            AccentChooser.DividerBeforeCustom = true;
            // Applying the accent repaints the whole app, so live picker updates are spaced out a little
            AccentChooser.PreviewIntervalMs = 45;
            AccentChooser.ColorChanged += OnAccentChooserChanged;
            AccentChooser.ColorCommitted += OnAccentChooserCommitted;
        }

        var presets = new List<ColorChoice>
        {
            // The Windows swatch carries a small mark so it reads as "follow Windows", not a colour
            new() { Value = Accent.SystemMode, Color = Accent.SystemColor, Name = loc["Appearance_AccentSystem"], Glyph = "", DividerAfter = true }
        };
        foreach (var (hex, nameKey) in Accent.Presets)
        {
            if (Accent.TryParse(hex, out var color)) presets.Add(new ColorChoice { Value = hex, Color = color, Name = loc[nameKey] });
        }
        AccentChooser.Presets = presets;
        AccentChooser.Recents = Storage.CurrentSettings.RecentAccentColors.ToList();
        UpdateAccentSelection();
    }

    private static bool IsPreset(string hex) =>
        Accent.Presets.Any(p => string.Equals(p.Hex, hex, StringComparison.OrdinalIgnoreCase));

    /// <summary>Live changes (swatch click, picker drag, put-back on cancel): only the running accent changes, no settings I/O.</summary>
    private void OnAccentChooserChanged(object? sender, ColorChooserEventArgs e)
    {
        if (e.Value == Accent.SystemMode) Accent.Apply(Accent.SystemMode);
        else Accent.Apply(e.Color);
    }

    private void OnAccentChooserCommitted(object? sender, ColorChooserEventArgs e)
    {
        if (e.IsCustom)
        {
            RememberRecentAccent(e.Value);
            AccentChooser.Recents = Storage.CurrentSettings.RecentAccentColors.ToList();
        }
        SelectAccent(e.Value);
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
        // While the picker is open the chooser shows the colour being previewed, not the saved one
        if (AccentChooser == null || AccentChooser.IsPickerOpen) return;
        string current = Storage.CurrentSettings.AccentColor ?? Accent.SystemMode;
        if (!Accent.TryParse(current, out _)) current = Accent.SystemMode;
        AccentChooser.SelectedValue = current;
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


    // ---------------------------------------------------------------------------------------------
    // Navigation position and sections
    // ---------------------------------------------------------------------------------------------

    private readonly List<(Border Frame, Border Bar, Models.SidebarPosition Position)> _navTiles = new();

    private void BuildNavPositionTiles()
    {
        if (NavPositionTiles == null) return;
        var loc = ViewModel.Loc;
        NavPositionTiles.Children.Clear();
        _navTiles.Clear();

        foreach (var (pos, key) in new[]
        {
            (Models.SidebarPosition.Left, "Nav_PositionLeft"), (Models.SidebarPosition.Right, "Nav_PositionRight"),
            (Models.SidebarPosition.Top, "Nav_PositionTop"), (Models.SidebarPosition.Bottom, "Nav_PositionBottom"),
        })
        {
            bool vertical = pos is Models.SidebarPosition.Left or Models.SidebarPosition.Right;
            var bar = new Border
            {
                CornerRadius = new CornerRadius(2),
                Width = vertical ? 11 : double.NaN,
                Height = vertical ? double.NaN : 6,
                HorizontalAlignment = pos switch
                {
                    Models.SidebarPosition.Left => HorizontalAlignment.Left,
                    Models.SidebarPosition.Right => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Stretch
                },
                VerticalAlignment = pos switch
                {
                    Models.SidebarPosition.Top => VerticalAlignment.Top,
                    Models.SidebarPosition.Bottom => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Stretch
                }
            };

            // A few "cards" so the preview reads as the library
            var cards = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = pos switch
                {
                    Models.SidebarPosition.Left => new Thickness(12, 0, 0, 0),
                    Models.SidebarPosition.Right => new Thickness(0, 0, 12, 0),
                    Models.SidebarPosition.Top => new Thickness(0, 7, 0, 0),
                    _ => new Thickness(0, 0, 0, 7)
                }
            };
            for (int i = 0; i < 3; i++)
            {
                var card = new Border
                {
                    Width = vertical ? 7 : 9,
                    Height = 7,
                    CornerRadius = new CornerRadius(1.5),
                    Opacity = 0.55
                };
                ThemeBrushes.Bind(card, Border.BackgroundProperty, "TextFillColorTertiaryBrush");
                cards.Children.Add(card);
            }

            var inner = new Grid { Margin = new Thickness(3) };
            inner.Children.Add(cards);
            inner.Children.Add(bar);

            var frame = new Border
            {
                Width = 56,
                Height = 40,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1.5),
                Child = inner
            };
            ThemeBrushes.Bind(frame, Border.BackgroundProperty, "ControlFillColorSecondaryBrush");

            var button = CreateSwatchButton(frame, loc[key]);
            button.Click += (s, e) => SelectNavPosition(pos);
            _navTiles.Add((frame, bar, pos));
            NavPositionTiles.Children.Add(button);
        }
        UpdateNavPositionTiles();
    }

    private void SelectNavPosition(Models.SidebarPosition position)
    {
        var settings = Storage.CurrentSettings;
        if (settings.SidebarPosition == position) return;
        settings.SidebarPosition = position;
        _ = Storage.SaveSettingsAsync(settings);
        UpdateNavPositionTiles();
    }

    /// <summary>Repaints the code-built parts of the appearance section after a theme change.</summary>
    private void UpdateAppearanceThemeVisuals(ElementTheme theme) => UpdateNavPositionTiles(theme);

    private void UpdateNavPositionTiles(ElementTheme? theme = null)
    {
        var current = Storage.CurrentSettings.SidebarPosition;
        var resolved = theme ?? ThemeBrushes.ThemeOf(this);
        var accent = ThemeBrushes.Get("AccentFillColorDefaultBrush", resolved);
        var idleStroke = ThemeBrushes.Get("ControlStrokeColorDefaultBrush", resolved);
        var idleBar = ThemeBrushes.Get("TextFillColorSecondaryBrush", resolved);
        foreach (var (frame, bar, pos) in _navTiles)
        {
            bool selected = pos == current;
            frame.BorderBrush = selected ? accent : idleStroke;
            bar.Background = selected ? accent : idleBar;
            bar.Opacity = selected ? 1 : 0.6;
        }
    }

    private void FillNavSectionCombos()
    {
        if (NavFavoritesComboBox == null) return;
        var loc = ViewModel.Loc;
        bool prev = _isInitializing;
        _isInitializing = true;
        var options = new[] { loc["Nav_ShowAlways"], loc["Nav_ShowWhenNotEmpty"], loc["Nav_HideSection"] };
        NavFavoritesComboBox.ItemsSource = options;
        NavMomentsComboBox.ItemsSource = options.ToArray();
        NavSharedComboBox.ItemsSource = options.ToArray();
        NavFavoritesComboBox.SelectedIndex = (int)Storage.CurrentSettings.FavoritesNavVisibility;
        NavMomentsComboBox.SelectedIndex = (int)Storage.CurrentSettings.MomentsNavVisibility;
        NavSharedComboBox.SelectedIndex = (int)Storage.CurrentSettings.SharedNavVisibility;
        _isInitializing = prev;
    }

    private void OnNavSectionVisibilityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var settings = Storage.CurrentSettings;
        var value = (Models.NavSectionVisibility)combo.SelectedIndex;
        if (combo == NavFavoritesComboBox) settings.FavoritesNavVisibility = value;
        else if (combo == NavSharedComboBox) settings.SharedNavVisibility = value;
        else settings.MomentsNavVisibility = value;
        _ = Storage.SaveSettingsAsync(settings);
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
