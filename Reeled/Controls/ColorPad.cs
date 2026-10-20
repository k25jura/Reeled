using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Reeled.Helpers;
using Reeled.Services;
using Windows.System;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

namespace Reeled.Controls;

/// <summary>
/// A lightweight colour picker: a saturation/value square, a hue slider, a preview swatch and a hex box.
///
/// It exists because WinUI's ColorPicker lags behind the pointer while dragging (its spectrum builds a
/// colour-name tooltip on every move). Here a drag only moves a thumb through a TranslateTransform (no
/// layout) and runs a few multiplications of HSV math. The hex text is refreshed once per frame while
/// dragging. There are no tooltips and no colour-name lookups.
/// </summary>
public sealed class ColorPad : UserControl
{
    private const double SquareHeight = 168;
    private const double HueHeight = 14;
    private const double SquareThumb = 18;
    private const double HueThumb = 20;
    private const double Radius = 6;

    private readonly double _width;

    // HSV is the source of truth: it survives round trips through RGB that would lose hue at s = 0 or v = 0.
    private double _h, _s = 1, _v = 1;
    private Color _color = Colors.Red;

    private readonly SolidColorBrush _hueBrush = new(Colors.Red);
    private readonly SolidColorBrush _previewBrush = new(Colors.Red);
    private readonly SolidColorBrush _hueThumbBrush = new(Colors.Red);
    private readonly TranslateTransform _squareThumb = new();
    private readonly TranslateTransform _hueThumbMove = new();
    private readonly ContentControl _square = new();
    private readonly ContentControl _hueSlider = new();
    private readonly TextBox _hex = new();

    private bool _draggingSquare, _draggingHue;
    private bool _hexDirty;
    private bool _renderHooked;
    private bool _settingHex;

    /// <summary>Raised on every change made by the user (drag, keys, typed hex). Not raised by <see cref="SetColor"/>.</summary>
    public event Action<Color>? ColorChanged;

    public Color Color => _color;

    public ColorPad(double width = 280)
    {
        _width = width;
        Width = width;
        Unloaded += (s, e) => UnhookRender();

        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(BuildSquare());
        root.Children.Add(BuildHueSlider());
        root.Children.Add(BuildHexRow());
        Content = root;

        SetColor(Colors.Red);
    }

    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>Moves the picker to a colour without raising <see cref="ColorChanged"/>.</summary>
    public void SetColor(Color color)
    {
        color = Color.FromArgb(255, color.R, color.G, color.B);
        RgbToHsv(color, out double h, out double s, out double v);
        // Keep the hue when the colour has none (grey, black) so the hue slider doesn't jump
        if (s > 0.0001 && v > 0.0001) _h = h;
        _s = s;
        _v = v;
        _color = color;
        Refresh(updateHex: true);
    }

    // ---------------------------------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------------------------------

    private UIElement BuildSquare()
    {
        var content = new Grid { Width = _width, Height = SquareHeight };
        content.Children.Add(new Rectangle { Fill = _hueBrush, RadiusX = Radius, RadiusY = Radius });
        content.Children.Add(new Rectangle
        {
            RadiusX = Radius,
            RadiusY = Radius,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 0),
                GradientStops =
                {
                    new GradientStop { Color = Colors.White, Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 1 }
                }
            }
        });
        content.Children.Add(new Rectangle
        {
            RadiusX = Radius,
            RadiusY = Radius,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0 },
                    new GradientStop { Color = Colors.Black, Offset = 1 }
                }
            }
        });
        content.Children.Add(new Rectangle
        {
            RadiusX = Radius,
            RadiusY = Radius,
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromArgb(48, 128, 128, 128)),
            IsHitTestVisible = false
        });
        content.Children.Add(MakeThumb(SquareThumb, _squareThumb, fill: null, VerticalAlignment.Top));

        _square.Content = content;
        _square.Padding = new Thickness(0);
        _square.IsTabStop = true;
        _square.UseSystemFocusVisuals = true;
        _square.FocusVisualMargin = new Thickness(-3);
        _square.CornerRadius = new CornerRadius(Radius);
        _square.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(_square, L("Color_Spectrum"));
        _square.PointerPressed += OnSquarePressed;
        _square.PointerMoved += OnSquareMoved;
        _square.PointerReleased += OnSquareReleased;
        _square.PointerCanceled += OnSquareReleased;
        _square.PointerCaptureLost += OnSquareReleased;
        _square.KeyDown += OnSquareKeyDown;
        return _square;
    }

    private UIElement BuildHueSlider()
    {
        var rainbow = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 0)
        };
        for (int i = 0; i <= 6; i++)
        {
            rainbow.GradientStops.Add(new GradientStop { Color = HsvToRgb(i * 60, 1, 1), Offset = i / 6.0 });
        }

        var content = new Grid { Width = _width, Height = HueThumb };
        content.Children.Add(new Rectangle
        {
            Height = HueHeight,
            RadiusX = HueHeight / 2,
            RadiusY = HueHeight / 2,
            Fill = rainbow,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        content.Children.Add(MakeThumb(HueThumb, _hueThumbMove, _hueThumbBrush, VerticalAlignment.Center));

        _hueSlider.Content = content;
        _hueSlider.Padding = new Thickness(0);
        _hueSlider.IsTabStop = true;
        _hueSlider.UseSystemFocusVisuals = true;
        _hueSlider.FocusVisualMargin = new Thickness(-3, -1, -3, -1);
        _hueSlider.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(_hueSlider, L("Color_Hue"));
        _hueSlider.PointerPressed += OnHuePressed;
        _hueSlider.PointerMoved += OnHueMoved;
        _hueSlider.PointerReleased += OnHueReleased;
        _hueSlider.PointerCanceled += OnHueReleased;
        _hueSlider.PointerCaptureLost += OnHueReleased;
        _hueSlider.KeyDown += OnHueKeyDown;
        return _hueSlider;
    }

    private UIElement BuildHexRow()
    {
        var preview = new Border
        {
            Width = 36,
            Height = 32,
            CornerRadius = new CornerRadius(Radius),
            Background = _previewBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(64, 128, 128, 128))
        };

        _hex.MaxLength = 7;
        _hex.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(_hex, L("Color_Hex"));
        _hex.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                CommitHex();
                e.Handled = true;
            }
        };
        _hex.LostFocus += (s, e) => CommitHex();
        _hex.TextChanged += (s, e) =>
        {
            if (_settingHex) return;
            // Apply as soon as a full valid colour has been typed or pasted
            string t = _hex.Text.Trim();
            if (t.Length == 7 && t[0] == '#' && Accent.TryParse(t, out var c)) ApplyTyped(c, keepFocusText: true);
        };

        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(preview);
        Grid.SetColumn(_hex, 1);
        row.Children.Add(_hex);
        return row;
    }

    private static UIElement MakeThumb(double size, TranslateTransform move, Brush? fill, VerticalAlignment vertical)
    {
        // A zero-size anchor at the top-left (or left-centre) of the surface; the transform slides it
        var host = new Canvas
        {
            Width = 0,
            Height = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = vertical,
            IsHitTestVisible = false,
            RenderTransform = move
        };
        var outer = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2.5
        };
        var inner = new Ellipse
        {
            Width = size + 3,
            Height = size + 3,
            Stroke = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(outer, -size / 2);
        Canvas.SetTop(outer, -size / 2);
        Canvas.SetLeft(inner, -(size + 3) / 2);
        Canvas.SetTop(inner, -(size + 3) / 2);
        host.Children.Add(inner);
        host.Children.Add(outer);
        return host;
    }

    // ---------------------------------------------------------------------------------------------
    // Square: saturation (x) and value (y)
    // ---------------------------------------------------------------------------------------------

    private void OnSquarePressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_square).Properties.IsLeftButtonPressed) return;
        if (!_square.CapturePointer(e.Pointer)) return;
        _square.Focus(FocusState.Pointer);
        _draggingSquare = true;
        HookRender();
        MoveSquare(e);
        e.Handled = true;
    }

    private void OnSquareMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingSquare) return;
        MoveSquare(e);
        e.Handled = true;
    }

    private void OnSquareReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingSquare) return;
        _draggingSquare = false;
        _square.ReleasePointerCapture(e.Pointer);
        FinishDrag();
        e.Handled = true;
    }

    private void MoveSquare(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_square).Position;
        _s = Clamp01(p.X / _width);
        _v = 1 - Clamp01(p.Y / SquareHeight);
        Changed(updateHex: false);
    }

    private void OnSquareKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double step = KeyStep(0.01, 0.1);
        switch (e.Key)
        {
            case VirtualKey.Left: _s = Clamp01(_s - step); break;
            case VirtualKey.Right: _s = Clamp01(_s + step); break;
            case VirtualKey.Up: _v = Clamp01(_v + step); break;
            case VirtualKey.Down: _v = Clamp01(_v - step); break;
            case VirtualKey.Home: _s = 0; break;
            case VirtualKey.End: _s = 1; break;
            case VirtualKey.PageUp: _v = 1; break;
            case VirtualKey.PageDown: _v = 0; break;
            default: return;
        }
        e.Handled = true;
        Changed(updateHex: true);
    }

    // ---------------------------------------------------------------------------------------------
    // Hue slider
    // ---------------------------------------------------------------------------------------------

    private void OnHuePressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_hueSlider).Properties.IsLeftButtonPressed) return;
        if (!_hueSlider.CapturePointer(e.Pointer)) return;
        _hueSlider.Focus(FocusState.Pointer);
        _draggingHue = true;
        HookRender();
        MoveHue(e);
        e.Handled = true;
    }

    private void OnHueMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingHue) return;
        MoveHue(e);
        e.Handled = true;
    }

    private void OnHueReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingHue) return;
        _draggingHue = false;
        _hueSlider.ReleasePointerCapture(e.Pointer);
        FinishDrag();
        e.Handled = true;
    }

    private void MoveHue(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_hueSlider).Position;
        _h = Clamp01(p.X / _width) * 360;
        Changed(updateHex: false);
    }

    private void OnHueKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double step = KeyStep(2, 20);
        switch (e.Key)
        {
            case VirtualKey.Left: case VirtualKey.Down: _h = Math.Max(0, _h - step); break;
            case VirtualKey.Right: case VirtualKey.Up: _h = Math.Min(360, _h + step); break;
            case VirtualKey.Home: _h = 0; break;
            case VirtualKey.End: _h = 360; break;
            default: return;
        }
        e.Handled = true;
        Changed(updateHex: true);
    }

    private static double KeyStep(double normal, double large)
    {
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0 ? large : normal;
    }

    // ---------------------------------------------------------------------------------------------
    // Hex box
    // ---------------------------------------------------------------------------------------------

    private void CommitHex()
    {
        string t = _hex.Text.Trim();
        if (Accent.TryParse(t, out var c))
        {
            if (c.R != _color.R || c.G != _color.G || c.B != _color.B) ApplyTyped(c, keepFocusText: false);
        }
        // Whether valid or not, show the real colour
        SetHexText(Accent.ToHex(_color));
    }

    private void ApplyTyped(Color c, bool keepFocusText)
    {
        RgbToHsv(c, out double h, out double s, out double v);
        if (s > 0.0001 && v > 0.0001) _h = h;
        _s = s;
        _v = v;
        _color = c;
        Refresh(updateHex: !keepFocusText);
        ColorChanged?.Invoke(_color);
    }

    private void SetHexText(string text)
    {
        if (_hex.Text == text) return;
        _settingHex = true;
        _hex.Text = text;
        _settingHex = false;
    }

    // ---------------------------------------------------------------------------------------------
    // Shared
    // ---------------------------------------------------------------------------------------------

    private void Changed(bool updateHex)
    {
        _color = HsvToRgb(_h, _s, _v);
        Refresh(updateHex);
        ColorChanged?.Invoke(_color);
    }

    /// <summary>Repositions the thumbs and recolours the brushes. No layout is invalidated.</summary>
    private void Refresh(bool updateHex)
    {
        _squareThumb.X = _s * _width;
        _squareThumb.Y = (1 - _v) * SquareHeight;
        _hueThumbMove.X = Math.Clamp(_h / 360, 0, 1) * _width;
        _hueThumbMove.Y = 0;

        _hueBrush.Color = HsvToRgb(_h, 1, 1);
        _hueThumbBrush.Color = HsvToRgb(_h, 1, 1);
        _previewBrush.Color = _color;

        if (updateHex) SetHexText(Accent.ToHex(_color));
        else _hexDirty = true;
    }

    private void HookRender()
    {
        if (_renderHooked) return;
        _renderHooked = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void UnhookRender()
    {
        if (!_renderHooked) return;
        _renderHooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        // The hex text is only refreshed once per frame while dragging
        if (!_hexDirty) return;
        _hexDirty = false;
        SetHexText(Accent.ToHex(_color));
    }

    private void FinishDrag()
    {
        if (_draggingSquare || _draggingHue) return;
        UnhookRender();
        if (_hexDirty)
        {
            _hexDirty = false;
            SetHexText(Accent.ToHex(_color));
        }
    }

    private static string L(string key)
    {
        try { return App.GetService<ILocalizationService>()[key]; }
        catch { return key; }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    // ---------------------------------------------------------------------------------------------
    // Colour math
    // ---------------------------------------------------------------------------------------------

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = h >= 360 ? 0 : h < 0 ? 0 : h;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        double r, g, b;
        switch ((int)(h / 60))
        {
            case 0: r = c; g = x; b = 0; break;
            case 1: r = x; g = c; b = 0; break;
            case 2: r = 0; g = c; b = x; break;
            case 3: r = 0; g = x; b = c; break;
            case 4: r = x; g = 0; b = c; break;
            default: r = c; g = 0; b = x; break;
        }
        return Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }
}
