using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Reeled.Controls;

/// <summary>
/// VLC-style wedge volume slider: a ramp that fills from green through yellow to red as the volume
/// climbs into boost, with a faint tick at 100%. Click or drag anywhere on it, or scroll.
/// </summary>
public sealed class VolumeWedge : Grid
{
    private readonly Polygon _track = new() { StrokeLineJoin = PenLineJoin.Round, StrokeThickness = 2 };
    private readonly Polygon _fill = new() { StrokeLineJoin = PenLineJoin.Round, StrokeThickness = 2 };
    private readonly Rectangle _tick = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
    private readonly LinearGradientBrush _gradient = new() { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
    private readonly TranslateTransform _fillOffset = new();

    private double _value;
    private double _maximum = 200;
    private double _shown;        // animated fraction actually drawn
    private double _from, _to;
    private long _animStart;
    private bool _animating;
    private bool _dragging;
    private bool _hovered;

    /// <summary>Raised when the user changes the volume (already snapped to <see cref="Step"/>).</summary>
    public event EventHandler<double>? ValueChangedByUser;

    public double Step { get; set; } = 5;

    public double Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, 0, _maximum);
            if (Math.Abs(value - _value) < 0.001 && _animating == false) { _value = value; return; }
            _value = value;
            AnimateTo(_value / _maximum);
        }
    }

    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(1, value);
            UpdateTick();
            _shown = _value / _maximum;
            Redraw();
        }
    }

    public VolumeWedge()
    {
        Width = 92;
        Height = 18;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        // Green while quiet, warming up through yellow into boost red
        AddStop("#5DA02E", 0.0);
        AddStop("#8DC63F", 0.35);
        AddStop("#C9C83C", 0.55);
        AddStop("#EFA23A", 0.75);
        AddStop("#E5483C", 1.0);

        var trackBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(46, 255, 255, 255));
        _track.Fill = trackBrush;
        _track.Stroke = trackBrush;
        _fill.Fill = _gradient;
        _fill.Stroke = _gradient;
        _tick.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 255, 255, 255));

        Children.Add(_track);
        Children.Add(_fill);
        Children.Add(_tick);

        SizeChanged += (s, e) => { UpdateTick(); Redraw(); };
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerReleased;
        PointerCaptureLost += (s, e) => _dragging = false;
        PointerEntered += (s, e) => { _hovered = true; UpdateHoverVisual(); };
        PointerExited += (s, e) => { _hovered = false; UpdateHoverVisual(); };
        PointerWheelChanged += OnWheel;
        Opacity = 0.9;
    }

    private void AddStop(string hex, double offset)
    {
        Helpers.Accent.TryParse(hex, out var c);
        _gradient.GradientStops.Add(new GradientStop { Color = c, Offset = offset });
    }

    private void UpdateHoverVisual()
    {
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(this, "Opacity", _hovered || _dragging ? 1.0 : 0.9, Helpers.Motion.Micro + 40));
        Helpers.Motion.BeginExact(sb);
    }

    private void UpdateTick()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (_maximum <= 100 || w <= 0)
        {
            _tick.Visibility = Visibility.Collapsed;
            return;
        }
        double x = 100.0 / _maximum * (w - 2) + 1;
        _tick.Visibility = Visibility.Visible;
        _tick.Height = h * 0.25 + (h * 0.75) * (100.0 / _maximum) + 3;
        _tick.Margin = new Thickness(x, 0, 0, -2);
    }

    /// <summary>The wedge: thin on the left, full height on the right, both shapes share the ramp.</summary>
    private void Redraw()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (w <= 2 || h <= 2) return;
        // Inset by the stroke so the rounded joins stay inside the bounds
        double l = 1, r = w - 1, b = h - 1, t = 1;
        double leftTop = b - (b - t) * 0.22;

        _track.Points = new PointCollection { new(l, leftTop), new(r, t), new(r, b), new(l, b) };

        double x = l + (r - l) * Math.Clamp(_shown, 0, 1);
        if (x - l < 0.5)
        {
            _fill.Visibility = Visibility.Collapsed;
            return;
        }
        _fill.Visibility = Visibility.Visible;
        double topAtX = leftTop + (t - leftTop) * ((x - l) / (r - l));
        _fill.Points = new PointCollection { new(l, leftTop), new(x, topAtX), new(x, b), new(l, b) };
        // The gradient spans the whole wedge, not just the filled part
        _gradient.MappingMode = BrushMappingMode.Absolute;
        _gradient.StartPoint = new Point(l, 0);
        _gradient.EndPoint = new Point(r, 0);
    }

    private void AnimateTo(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (Helpers.Motion.IsReduced || _dragging || !IsLoaded)
        {
            StopAnimation();
            _shown = fraction;
            Redraw();
            return;
        }
        _from = _shown;
        _to = fraction;
        _animStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        double t = (System.Diagnostics.Stopwatch.GetTimestamp() - _animStart) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0 / 160.0;
        if (t >= 1)
        {
            _shown = _to;
            StopAnimation();
        }
        else
        {
            double eased = 1 - Math.Pow(1 - t, 3);
            _shown = _from + (_to - _from) * eased;
        }
        Redraw();
    }

    private double ValueAt(double x)
    {
        double w = ActualWidth;
        if (w <= 0) return _value;
        double raw = Math.Clamp(x / w, 0, 1) * _maximum;
        return Math.Clamp(Math.Round(raw / Step) * Step, 0, _maximum);
    }

    private void Commit(double value)
    {
        if (Math.Abs(value - _value) < 0.001) return;
        _value = value;
        _shown = value / _maximum;
        Redraw();
        ValueChangedByUser?.Invoke(this, value);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        CapturePointer(e.Pointer);
        _dragging = true;
        StopAnimation();
        Commit(ValueAt(e.GetCurrentPoint(this).Position.X));
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        Commit(ValueAt(e.GetCurrentPoint(this).Position.X));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        UpdateHoverVisual();
        e.Handled = true;
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta == 0) return;
        double next = Math.Clamp(_value + (delta > 0 ? Step : -Step), 0, _maximum);
        AnimateTo(next / _maximum);
        _value = next;
        ValueChangedByUser?.Invoke(this, next);
        e.Handled = true;
    }
}
