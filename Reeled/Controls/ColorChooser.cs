using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Reeled.Helpers;
using Reeled.Services;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

namespace Reeled.Controls;

/// <summary>One preset swatch. <see cref="Value"/> is a "#RRGGBB" colour, or a key the host understands (e.g. "System").</summary>
public sealed class ColorChoice
{
    public required string Value { get; init; }
    public required Color Color { get; init; }
    public required string Name { get; init; }
    /// <summary>A small mark on the swatch, for choices that aren't a plain colour.</summary>
    public string? Glyph { get; init; }
    public bool DividerAfter { get; init; }
}

public sealed class ColorChooserEventArgs : EventArgs
{
    public ColorChooserEventArgs(string value, Color color, bool isCustom, bool isPreview)
    {
        Value = value;
        Color = color;
        IsCustom = isCustom;
        IsPreview = isPreview;
    }

    public string Value { get; }
    public Color Color { get; }
    /// <summary>The colour came from the picker, not from a preset swatch.</summary>
    public bool IsCustom { get; }
    /// <summary>Raised while the picker is being dragged; the final value follows as a non-preview change.</summary>
    public bool IsPreview { get; }
}

/// <summary>
/// A row of colour swatches (presets, recent picks) ending in a rainbow button that opens a full colour
/// picker. Shared by the accent colour, moment colours and subtitle colour.
///
/// While the picker is dragged, changes are coalesced to one <see cref="ColorChanged"/> per frame (or per
/// <see cref="PreviewIntervalMs"/>), so hosts can apply them live without keeping up with every pointer
/// move. <see cref="ColorCommitted"/> fires once for the final choice.
/// </summary>
public sealed class ColorChooser : UserControl
{
    private sealed class Swatch
    {
        public string? Value;
        public bool IsCustom;
        public required Button Button;
        public required Ellipse Ring;
        public required Ellipse Fill;
        public required FontIcon Check;
        public required ScaleTransform CheckScale;
        public required SolidColorBrush Ink;
        public FontIcon? Mark;
        public bool IsSelected;
        public bool IsHovered;
    }

    private static ControlTemplate? s_buttonTemplate;

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<Swatch> _swatches = new();
    private readonly HashSet<string> _knownValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly SolidColorBrush _customBrush = new(Colors.Transparent);
    private readonly LinearGradientBrush _rainbow = new() { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
    private Swatch? _custom;
    private bool _customShowsColor;

    private IReadOnlyList<ColorChoice> _presets = Array.Empty<ColorChoice>();
    private IReadOnlyList<string> _recents = Array.Empty<string>();
    private string? _selected;
    private double _swatchSize = 24;
    private double _swatchSpacing = 6;
    private bool _allowCustom = true;
    private bool _dividerBeforeCustom;
    private bool _rebuildPending;

    // Picker state
    private Flyout? _flyout;
    private ColorPicker? _picker;
    private StackPanel? _panel;
    private TextBlock? _title;
    private Grid? _buttons;
    private Button? _applyButton;
    private Button? _cancelButton;
    private bool _pickerOpen;
    private bool _committed;
    private bool _seeding;
    private bool _hasSeed;
    private Color _seed;
    private bool _dirty;
    private Color _pending;
    private long _lastFlush;
    private string? _valueBeforePicker;

    /// <summary>Raised on every change of the selection, including live updates while the picker is dragged.</summary>
    public event EventHandler<ColorChooserEventArgs>? ColorChanged;
    /// <summary>Raised once for the final choice: a swatch click, Apply, or the picker closing (when it applies live).</summary>
    public event EventHandler<ColorChooserEventArgs>? ColorCommitted;
    public event EventHandler? PickerOpened;
    public event EventHandler? PickerClosed;

    public ColorChooser()
    {
        string[] stops = { "#FF5252", "#FFB300", "#66BB6A", "#29B6F6", "#7E57C2", "#EC407A" };
        for (int i = 0; i < stops.Length; i++)
        {
            Accent.TryParse(stops[i], out var c);
            _rainbow.GradientStops.Add(new GradientStop { Color = c, Offset = i / (double)(stops.Length - 1) });
        }

        Content = _row;
        _row.Spacing = _swatchSpacing;
        Unloaded += (s, e) => StopPreviewLoop();
        Rebuild();
    }

    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    public IReadOnlyList<ColorChoice> Presets
    {
        get => _presets;
        set { _presets = value ?? Array.Empty<ColorChoice>(); RequestRebuild(); }
    }

    /// <summary>Recently picked custom colours ("#RRGGBB"), newest first. Kept by the host.</summary>
    public IReadOnlyList<string> Recents
    {
        get => _recents;
        set { _recents = value ?? Array.Empty<string>(); RequestRebuild(); }
    }

    public int MaxRecents { get; set; } = 4;

    /// <summary>The selected preset's <see cref="ColorChoice.Value"/>, or a "#RRGGBB" colour.</summary>
    public string? SelectedValue
    {
        get => _selected;
        set
        {
            if (string.Equals(_selected, value, StringComparison.OrdinalIgnoreCase)) return;
            _selected = value;
            UpdateSelection(animate: true);
        }
    }

    public Color SelectedColor
    {
        get => ResolveColor(_selected);
        set => SelectedValue = Accent.ToHex(value);
    }

    /// <summary>Diameter of a swatch's colour disc; the selection ring adds 4px on each side.</summary>
    public double SwatchSize
    {
        get => _swatchSize;
        set { _swatchSize = value; RequestRebuild(); }
    }

    public double SwatchSpacing
    {
        get => _swatchSpacing;
        set { _swatchSpacing = value; _row.Spacing = value; }
    }

    public bool AllowCustom
    {
        get => _allowCustom;
        set { _allowCustom = value; RequestRebuild(); }
    }

    public bool DividerBeforeCustom
    {
        get => _dividerBeforeCustom;
        set { _dividerBeforeCustom = value; RequestRebuild(); }
    }

    /// <summary>True: the picker has Apply and Cancel (dismissing puts the previous colour back). False: it applies as you drag.</summary>
    public bool ConfirmCustom { get; set; } = true;

    /// <summary>Minimum time between live picker updates; 0 delivers at most one per frame.</summary>
    public double PreviewIntervalMs { get; set; }

    public FlyoutPlacementMode FlyoutPlacement { get; set; } = FlyoutPlacementMode.Bottom;
    /// <summary>Over video the picker sits on frosted glass and uses the player's flyout style.</summary>
    public bool UseVideoGlass { get; set; }
    public Style? FlyoutPresenterStyle { get; set; }
    public Thickness PickerPadding { get; set; }

    public bool IsPickerOpen => _pickerOpen;

    // ---------------------------------------------------------------------------------------------
    // Swatches
    // ---------------------------------------------------------------------------------------------

    private static string L(string key)
    {
        try { return App.GetService<ILocalizationService>()[key]; }
        catch { return key; }
    }

    private void RequestRebuild()
    {
        // The anchor of an open picker must not disappear underneath it
        if (_pickerOpen) { _rebuildPending = true; return; }
        Rebuild();
    }

    private void Rebuild()
    {
        _rebuildPending = false;
        _row.Children.Clear();
        _swatches.Clear();
        _knownValues.Clear();
        _custom = null;
        _customShowsColor = false;

        foreach (var choice in _presets)
        {
            _knownValues.Add(choice.Value);
            AddSwatch(choice.Value, choice.Color, choice.Name, choice.Glyph, custom: false);
            if (choice.DividerAfter) AddDivider();
        }

        var recents = _recents.Where(h => Accent.TryParse(h, out _) && !_knownValues.Contains(h)).Take(MaxRecents).ToList();
        if (recents.Count > 0)
        {
            if (_presets.Count > 0 && !_presets[^1].DividerAfter) AddDivider();
            string custom = L("Color_Custom");
            foreach (var hex in recents)
            {
                _knownValues.Add(hex);
                Accent.TryParse(hex, out var color);
                AddSwatch(hex, color, $"{custom} · {hex}", null, custom: false);
            }
        }

        if (_allowCustom)
        {
            if (_dividerBeforeCustom) AddDivider();
            _custom = AddSwatch(null, Colors.Transparent, L("Color_Pick"), null, custom: true);
            _custom.Fill.Fill = _rainbow;
            _custom.Mark!.Glyph = "";
        }

        UpdateSelection(animate: false);
    }

    private void AddDivider()
    {
        var divider = (Border)XamlReader.Load(
            "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='1' VerticalAlignment='Center' " +
            "Background='{ThemeResource DividerStrokeColorDefaultBrush}' />");
        divider.Height = (_swatchSize + 8) * 0.62;
        divider.Margin = new Thickness(4, 0, 4, 0);
        _row.Children.Add(divider);
    }

    private Swatch AddSwatch(string? value, Color color, string name, string? glyph, bool custom)
    {
        double inner = _swatchSize, outer = inner + 8;
        double checkSize = Math.Max(10, Math.Round(inner * 0.5));

        var ring = (Ellipse)XamlReader.Load(
            "<Ellipse xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' StrokeThickness='2' " +
            "Stroke='{ThemeResource TextFillColorPrimaryBrush}' IsHitTestVisible='False' />");
        ring.Width = ring.Height = outer;
        ring.Opacity = 0;

        var fill = new Ellipse
        {
            Width = inner,
            Height = inner,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            StrokeThickness = 1
        };

        bool darkInk = Accent.Luminance(color) > 0.62;
        var ink = new SolidColorBrush(darkInk ? Color.FromArgb(230, 0, 0, 0) : Colors.White);
        var checkScale = new ScaleTransform { ScaleX = 0.4, ScaleY = 0.4, CenterX = checkSize / 2, CenterY = checkSize / 2 };
        var check = new FontIcon
        {
            Glyph = "",
            FontSize = checkSize,
            Width = checkSize,
            Height = checkSize,
            Foreground = ink,
            Opacity = 0,
            RenderTransform = checkScale,
            IsHitTestVisible = false
        };

        var content = new Grid { Width = outer, Height = outer };
        content.Children.Add(ring);
        content.Children.Add(fill);
        FontIcon? mark = null;
        var markInk = custom ? new SolidColorBrush(Colors.White) : ink;
        if (glyph != null || custom)
        {
            // Marks the swatches that aren't a plain colour (follow Windows, custom picker)
            content.Children.Add(mark = new FontIcon
            {
                Glyph = glyph ?? "",
                FontSize = Math.Max(9, Math.Round(inner * 0.42)),
                Foreground = markInk,
                Opacity = 0.9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });
        }
        content.Children.Add(check);

        var button = CreateButton(content, name);
        var swatch = new Swatch
        {
            Value = value, IsCustom = custom, Button = button, Ring = ring, Fill = fill,
            Check = check, CheckScale = checkScale, Ink = markInk, Mark = mark
        };
        button.Click += (s, e) => OnSwatchClick(swatch);
        button.PointerEntered += (s, e) => { swatch.IsHovered = true; Fade(swatch.Ring, swatch.IsSelected ? 1 : 0.35); };
        button.PointerExited += (s, e) => { swatch.IsHovered = false; Fade(swatch.Ring, swatch.IsSelected ? 1 : 0); };
        _swatches.Add(swatch);
        _row.Children.Add(button);
        return swatch;
    }

    private static Button CreateButton(UIElement content, string name)
    {
        s_buttonTemplate ??= (ControlTemplate)XamlReader.Load(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
            "<ContentPresenter Background='Transparent' HorizontalAlignment='Center' VerticalAlignment='Center' /></ControlTemplate>");
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Template = s_buttonTemplate
        };
        ToolTipService.SetToolTip(button, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    private void OnSwatchClick(Swatch swatch)
    {
        if (swatch.IsCustom)
        {
            OpenPicker(swatch.Button);
            return;
        }
        if (string.Equals(_selected, swatch.Value, StringComparison.OrdinalIgnoreCase)) return;
        _selected = swatch.Value;
        UpdateSelection(animate: true);
        var args = new ColorChooserEventArgs(swatch.Value!, ResolveColor(swatch.Value), isCustom: false, isPreview: false);
        ColorChanged?.Invoke(this, args);
        ColorCommitted?.Invoke(this, args);
    }

    private Color ResolveColor(string? value)
    {
        if (value == null) return default;
        foreach (var choice in _presets)
        {
            if (string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase)) return choice.Color;
        }
        return Accent.TryParse(value, out var c) ? c : default;
    }

    private void UpdateSelection(bool animate)
    {
        bool customSelected = _selected != null && !_knownValues.Contains(_selected) && Accent.TryParse(_selected, out _);

        foreach (var swatch in _swatches)
        {
            bool selected = swatch.IsCustom ? customSelected : string.Equals(swatch.Value, _selected, StringComparison.OrdinalIgnoreCase);
            if (selected == swatch.IsSelected) continue;
            swatch.IsSelected = selected;
            ApplyState(swatch, animate);
        }

        if (_custom != null) UpdateCustomVisual(customSelected);
    }

    /// <summary>The custom button shows the chosen colour when it isn't one of the swatches, else the rainbow.</summary>
    private void UpdateCustomVisual(bool customSelected)
    {
        var custom = _custom!;
        if (customSelected)
        {
            Accent.TryParse(_selected, out var color);
            _customBrush.Color = color;
            custom.Ink.Color = Accent.Luminance(color) > 0.62 ? Color.FromArgb(230, 0, 0, 0) : Colors.White;
            if (!_customShowsColor)
            {
                _customShowsColor = true;
                custom.Fill.Fill = _customBrush;
                custom.Mark!.Glyph = "";
            }
        }
        else if (_customShowsColor)
        {
            _customShowsColor = false;
            custom.Fill.Fill = _rainbow;
            custom.Ink.Color = Colors.White;
            custom.Mark!.Glyph = "";
        }
    }

    private static void ApplyState(Swatch swatch, bool animate)
    {
        double ring = swatch.IsSelected ? 1 : swatch.IsHovered ? 0.35 : 0;
        double check = swatch.IsSelected && !swatch.IsCustom ? 1 : 0;
        double scale = check == 1 ? 1 : 0.4;
        double mark = swatch.IsCustom ? 0.9 : swatch.IsSelected ? 0 : 0.9;

        if (!animate || Motion.IsReduced)
        {
            swatch.Ring.Opacity = ring;
            swatch.Check.Opacity = check;
            swatch.CheckScale.ScaleX = swatch.CheckScale.ScaleY = scale;
            if (swatch.Mark != null) swatch.Mark.Opacity = mark;
            return;
        }

        var sb = new Storyboard();
        sb.Children.Add(Motion.Spline(swatch.Ring, "Opacity", ring, Motion.Micro + 60));
        sb.Children.Add(Motion.Spline(swatch.Check, "Opacity", check, Motion.Short));
        sb.Children.Add(Motion.Spline(swatch.CheckScale, "ScaleX", scale, Motion.Medium));
        sb.Children.Add(Motion.Spline(swatch.CheckScale, "ScaleY", scale, Motion.Medium));
        if (swatch.Mark != null) sb.Children.Add(Motion.Spline(swatch.Mark, "Opacity", mark, Motion.Short));
        Motion.BeginExact(sb);
    }

    private static void Fade(UIElement element, double to)
    {
        var sb = new Storyboard();
        sb.Children.Add(Motion.Spline(element, "Opacity", to, Motion.Micro + 60));
        Motion.BeginExact(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // Picker
    // ---------------------------------------------------------------------------------------------

    private void EnsureFlyout()
    {
        if (_flyout != null) return;

        _picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true,
            IsColorSliderVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Box,
            Width = 300
        };
        _picker.ColorChanged += OnPickerColorChanged;

        _title = new TextBlock { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        _applyButton = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _cancelButton = new Button { HorizontalAlignment = HorizontalAlignment.Stretch };
        _applyButton.Click += OnApplyClick;
        _cancelButton.Click += (s, e) => _flyout?.Hide();

        _buttons = new Grid { ColumnSpacing = 8 };
        _buttons.ColumnDefinitions.Add(new ColumnDefinition());
        _buttons.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_applyButton, 1);
        _buttons.Children.Add(_cancelButton);
        _buttons.Children.Add(_applyButton);

        _panel = new StackPanel { Spacing = 14, Padding = PickerPadding };
        _panel.Children.Add(_title);
        _panel.Children.Add(_picker);
        _panel.Children.Add(_buttons);

        _flyout = new Flyout { Content = _panel };
        if (FlyoutPresenterStyle != null) _flyout.FlyoutPresenterStyle = FlyoutPresenterStyle;
        if (UseVideoGlass) VideoGlass.Wrap(_flyout, 7);
        _flyout.Opened += OnFlyoutOpened;
        _flyout.Closed += OnFlyoutClosed;
    }

    private void OpenPicker(FrameworkElement anchor)
    {
        EnsureFlyout();
        _title!.Text = L("Color_Pick");
        _applyButton!.Content = L("Color_Apply");
        _cancelButton!.Content = L("Common_Cancel");
        _title.Visibility = _buttons!.Visibility = ConfirmCustom ? Visibility.Visible : Visibility.Collapsed;
        _panel!.RequestedTheme = ActualTheme;
        _flyout!.Placement = FlyoutPlacement;

        _valueBeforePicker = _selected;
        _committed = false;
        _dirty = false;

        // Starting the picker on the current colour must not count as a change
        _seeding = true;
        var seed = ResolveColor(_selected);
        _picker!.Color = _seed = seed.A == 0 ? Accent.Current : seed;
        _seeding = false;
        _hasSeed = true;

        _pickerOpen = true;
        _flyout.ShowAt(anchor);
    }

    private void OnFlyoutOpened(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnPreviewFrame;
        CompositionTarget.Rendering += OnPreviewFrame;
        PickerOpened?.Invoke(this, EventArgs.Empty);
    }

    private void OnPickerColorChanged(ColorPicker sender, ColorChangedEventArgs e)
    {
        if (_seeding) return;
        if (_hasSeed)
        {
            // The picker re-reports the starting colour (rounded through HSV) when it loads; that isn't a pick
            var c = e.NewColor;
            if (Math.Abs(c.R - _seed.R) <= 1 && Math.Abs(c.G - _seed.G) <= 1 && Math.Abs(c.B - _seed.B) <= 1) return;
            _hasSeed = false;
        }
        // Only remember the latest colour; it is delivered once per frame
        _pending = e.NewColor;
        _dirty = true;
    }

    private void OnPreviewFrame(object? sender, object e)
    {
        if (!_dirty) return;
        if (PreviewIntervalMs > 0 && Environment.TickCount64 - _lastFlush < PreviewIntervalMs) return;
        FlushPreview(isPreview: true);
    }

    private void FlushPreview(bool isPreview)
    {
        if (!_dirty) return;
        _dirty = false;
        _lastFlush = Environment.TickCount64;
        string hex = Accent.ToHex(_pending);
        if (string.Equals(hex, _selected, StringComparison.OrdinalIgnoreCase)) return;
        _selected = hex;
        UpdateSelection(animate: false);
        ColorChanged?.Invoke(this, new ColorChooserEventArgs(hex, _pending, isCustom: true, isPreview));
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        _pending = _picker!.Color;
        _dirty = true;
        FlushPreview(isPreview: false);
        _committed = true;
        if (_selected != null)
        {
            ColorCommitted?.Invoke(this, new ColorChooserEventArgs(_selected, _picker.Color, isCustom: true, isPreview: false));
        }
        _flyout?.Hide();
    }

    private void OnFlyoutClosed(object? sender, object e)
    {
        StopPreviewLoop();
        _pickerOpen = false;

        if (!_committed)
        {
            if (ConfirmCustom)
            {
                // Dismissed without applying: put the previous colour back
                _dirty = false;
                if (!string.Equals(_selected, _valueBeforePicker, StringComparison.OrdinalIgnoreCase))
                {
                    _selected = _valueBeforePicker;
                    UpdateSelection(animate: true);
                    if (_selected != null)
                    {
                        ColorChanged?.Invoke(this, new ColorChooserEventArgs(_selected, ResolveColor(_selected), isCustom: false, isPreview: false));
                    }
                }
            }
            else
            {
                FlushPreview(isPreview: false);
                if (_selected != null && !string.Equals(_selected, _valueBeforePicker, StringComparison.OrdinalIgnoreCase))
                {
                    ColorCommitted?.Invoke(this, new ColorChooserEventArgs(_selected, ResolveColor(_selected), isCustom: true, isPreview: false));
                }
            }
        }

        if (_rebuildPending) Rebuild();
        PickerClosed?.Invoke(this, EventArgs.Empty);
    }

    private void StopPreviewLoop() => CompositionTarget.Rendering -= OnPreviewFrame;
}
