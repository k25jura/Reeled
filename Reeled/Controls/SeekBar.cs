using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Reeled.Controls;

/// <summary>
/// The player's timeline. A thin track that thickens under the pointer, an accent progress fill, a
/// "where you would land" preview fill with a time bubble, and a thumb that grows on hover and gets a
/// soft halo while dragging. Everything that moves per frame is a render transform, so playback
/// updates never cause layout.
/// </summary>
public sealed class SeekBar : Grid
{
    private const double TrackHeight = 4;
    private const double HoverTrackScale = 1.6;
    private const double ThumbSize = 14;

    private readonly Grid _trackHost = new() { Height = TrackHeight * HoverTrackScale + 6, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly Border _track = new() { Height = TrackHeight, CornerRadius = new CornerRadius(TrackHeight / 2), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _hoverFill = new() { Height = TrackHeight, CornerRadius = new CornerRadius(TrackHeight / 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _fill = new() { Height = TrackHeight, CornerRadius = new CornerRadius(TrackHeight / 2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScaleTransform _trackScale = new() { ScaleY = 1 };
    private readonly ScaleTransform _hoverFillScale = new() { ScaleX = 0, ScaleY = 1 };
    private readonly ScaleTransform _fillScale = new() { ScaleX = 0, ScaleY = 1 };

    private readonly Grid _thumbHost = new() { Width = ThumbSize * 2.4, Height = ThumbSize * 2.4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly Ellipse _halo = new() { Width = ThumbSize * 2.4, Height = ThumbSize * 2.4, Opacity = 0 };
    private readonly Ellipse _thumb = new() { Width = ThumbSize, Height = ThumbSize };
    private readonly TranslateTransform _thumbTranslate = new();
    private readonly ScaleTransform _thumbScale = new() { ScaleX = 0.72, ScaleY = 0.72, CenterX = ThumbSize / 2, CenterY = ThumbSize / 2 };
    private readonly ScaleTransform _haloScale = new() { ScaleX = 0.5, ScaleY = 0.5, CenterX = ThumbSize * 1.2, CenterY = ThumbSize * 1.2 };

    private readonly Border _bubble = new()
    {
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 3, 8, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        Opacity = 0
    };
    private readonly TextBlock _bubbleText = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TranslateTransform _bubbleTranslate = new() { Y = -26 };

    private bool _isHovered;
    private bool _isDragging;
    private double _value;
    private double _hoverX;

    /// <summary>Moment markers are drawn here, aligned with the track.</summary>
    public Canvas MarkersCanvas { get; } = new() { Height = 8, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };

    /// <summary>Text for the bubble at a position (0..100), e.g. the time and a moment's name.</summary>
    public Func<double, string>? FormatPreview { get; set; }

    public event EventHandler? DragStarted;
    public event EventHandler<double>? DragDelta;
    public event EventHandler<double>? DragCompleted;

    public bool IsDragging => _isDragging;

    /// <summary>0..100. Setting it while the user drags is ignored, so playback can't fight the pointer.</summary>
    public double Value
    {
        get => _value;
        set
        {
            if (_isDragging) return;
            SetVisualValue(value);
        }
    }

    public SeekBar()
    {
        Height = 32;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        _track.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(64, 255, 255, 255));
        _hoverFill.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255));
        _fill.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        _halo.Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        _thumb.Fill = new SolidColorBrush(Microsoft.UI.Colors.White);
        _thumb.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 0, 0, 0));
        _thumb.StrokeThickness = 1;

        _track.RenderTransform = _trackScale;
        _track.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _hoverFill.RenderTransform = new TransformGroup { Children = { _hoverFillScale } };
        _hoverFill.RenderTransformOrigin = new Windows.Foundation.Point(0, 0.5);
        _fill.RenderTransform = _fillScale;
        _fill.RenderTransformOrigin = new Windows.Foundation.Point(0, 0.5);

        _trackHost.Children.Add(_track);
        _trackHost.Children.Add(_hoverFill);
        _trackHost.Children.Add(_fill);
        Children.Add(_trackHost);
        Children.Add(MarkersCanvas);

        _halo.RenderTransform = _haloScale;
        _thumb.RenderTransform = _thumbScale;
        _thumb.HorizontalAlignment = HorizontalAlignment.Center;
        _thumb.VerticalAlignment = VerticalAlignment.Center;
        _thumbHost.Children.Add(_halo);
        _thumbHost.Children.Add(_thumb);
        _thumbHost.RenderTransform = _thumbTranslate;
        _halo.Opacity = 0;
        Children.Add(_thumbHost);

        _bubble.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 28, 28, 28));
        _bubble.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255));
        _bubble.BorderThickness = new Thickness(1);
        _bubbleText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        _bubble.Child = _bubbleText;
        _bubble.RenderTransform = _bubbleTranslate;
        Children.Add(_bubble);

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        SizeChanged += (s, e) =>
        {
            Clip = null;
            SetVisualValue(_value);
            UpdateHover(_hoverX);
        };
    }

    private void SetVisualValue(double value)
    {
        _value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 100);
        double width = ActualWidth;
        if (width <= 0) return;
        double fraction = _value / 100.0;
        _fill.Width = width;
        _hoverFill.Width = width;
        _fillScale.ScaleX = fraction;
        _thumbTranslate.X = fraction * width - _thumbHost.Width / 2;
    }

    private double ValueAt(double x) => ActualWidth <= 0 ? 0 : Math.Clamp(x / ActualWidth * 100.0, 0, 100);

    private void UpdateHover(double x)
    {
        _hoverX = x;
        double width = ActualWidth;
        if (width <= 0) return;
        double fraction = Math.Clamp(x / width, 0, 1);
        _hoverFillScale.ScaleX = fraction;

        string text = FormatPreview?.Invoke(fraction * 100.0) ?? string.Empty;
        _bubbleText.Text = text;
        _bubble.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double bw = _bubble.DesiredSize.Width;
        _bubbleTranslate.X = Math.Clamp(fraction * width - bw / 2, -8, width - bw + 8);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = true;
        UpdateHover(e.GetCurrentPoint(this).Position.X);
        AnimateState();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = false;
        AnimateState();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        CapturePointer(e.Pointer);
        _isDragging = true;
        double v = ValueAt(point.Position.X);
        SetVisualValue(v);
        UpdateHover(point.Position.X);
        AnimateState();
        DragStarted?.Invoke(this, EventArgs.Empty);
        DragDelta?.Invoke(this, v);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        double x = e.GetCurrentPoint(this).Position.X;
        UpdateHover(x);
        if (!_isDragging) return;
        double v = ValueAt(x);
        if (Math.Abs(v - _value) < 0.01) return;
        SetVisualValue(v);
        DragDelta?.Invoke(this, v);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        double v = ValueAt(e.GetCurrentPoint(this).Position.X);
        EndDrag(v);
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging) EndDrag(_value);
    }

    private void EndDrag(double value)
    {
        _isDragging = false;
        SetVisualValue(value);
        AnimateState();
        DragCompleted?.Invoke(this, value);
    }

    /// <summary>Idle: thin track, small thumb. Hover: thicker track, full thumb, preview. Drag: halo.</summary>
    private void AnimateState()
    {
        bool active = _isHovered || _isDragging;
        double trackScale = active ? HoverTrackScale : 1.0;
        double thumbScale = _isDragging ? 1.12 : active ? 1.0 : 0.72;

        var sb = new Storyboard();
        double ms = active ? Helpers.Motion.Short : Helpers.Motion.Short - 40;
        sb.Children.Add(Helpers.Motion.Spline(_trackScale, "ScaleY", trackScale, ms));
        sb.Children.Add(Helpers.Motion.Spline(_hoverFillScale, "ScaleY", trackScale, ms));
        sb.Children.Add(Helpers.Motion.Spline(_fillScale, "ScaleY", trackScale, ms));
        sb.Children.Add(Helpers.Motion.Spline(_thumbScale, "ScaleX", thumbScale, ms));
        sb.Children.Add(Helpers.Motion.Spline(_thumbScale, "ScaleY", thumbScale, ms));
        sb.Children.Add(Helpers.Motion.Spline(_halo, "Opacity", _isDragging ? 0.28 : 0, ms));
        sb.Children.Add(Helpers.Motion.Spline(_haloScale, "ScaleX", _isDragging ? 1 : 0.5, Helpers.Motion.Medium));
        sb.Children.Add(Helpers.Motion.Spline(_haloScale, "ScaleY", _isDragging ? 1 : 0.5, Helpers.Motion.Medium));
        sb.Children.Add(Helpers.Motion.Spline(_hoverFill, "Opacity", active && !_isDragging ? 1 : 0, ms));
        sb.Children.Add(Helpers.Motion.Spline(_bubble, "Opacity", active ? 1 : 0, ms));
        sb.Children.Add(Helpers.Motion.Spline(_bubbleTranslate, "Y", active ? -28 : -22, ms));
        Helpers.Motion.BeginExact(sb);
    }
}
