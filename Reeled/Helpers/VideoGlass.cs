using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Reeled.Helpers;

/// <summary>
/// Frosted glass over the video. XAML's AcrylicBrush can't sample a SwapChainPanel (it renders black over
/// the video), so a small GPU copy of the video (<see cref="VideoFrameMirror"/>) is blurred instead.
/// Elements opt in with <c>VideoGlass.Kind="Bar"</c> or <c>"Flyout"</c>; the element must be an empty layer
/// placed beneath the real content, because a composition child visual draws on top of its host.
/// </summary>
public sealed class VideoGlass
{
    public static VideoGlass? Current { get; private set; }

    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(string), typeof(VideoGlass), new PropertyMetadata(null, OnKindChanged));

    public static string? GetKind(DependencyObject obj) => (string?)obj.GetValue(KindProperty);
    public static void SetKind(DependencyObject obj, string? value) => obj.SetValue(KindProperty, value);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(double), typeof(VideoGlass), new PropertyMetadata(0.0));

    public static double GetCornerRadius(DependencyObject obj) => (double)obj.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject obj, double value) => obj.SetValue(CornerRadiusProperty, value);

    private sealed class Pane
    {
        public required FrameworkElement Host;
        public required Backdrop.Surface Surface;
        public ContainerVisual? Container;
        public SpriteVisual? Sprite;
        public CompositionEffectBrush? Brush;
        public CompositionRoundedRectangleGeometry? ClipGeometry;
    }

    private readonly FrameworkElement _video;
    private readonly Compositor _compositor;
    private readonly List<Pane> _panes = new();
    private CompositionEffectFactory? _factory;
    private VideoFrameMirror? _mirror;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _frameTimer;
    private bool _isActive = true;
    private long _trackUntil;
    private bool _isTracking;

    /// <summary>False when the swap chain couldn't be bound; hosts then show the solid fallback colour.</summary>
    public bool IsAvailable => _mirror?.Surface != null && _factory != null;

    private VideoGlass(FrameworkElement video)
    {
        _video = video;
        _compositor = ElementCompositionPreview.GetElementVisual(video).Compositor;
        _video.SizeChanged += (_, _) => SyncAll();
    }

    /// <summary>Creates the page's glass instance; hosts that load afterwards register with it.</summary>
    public static VideoGlass Create(FrameworkElement video)
    {
        Current = new VideoGlass(video);
        return Current;
    }

    /// <summary>
    /// Slips an empty glass layer under a border's content (the border keeps its padding, tint fallback and
    /// corner radius). Safe to call before <see cref="Create"/>: the layer registers once it loads.
    /// </summary>
    public static void Wrap(Microsoft.UI.Xaml.Controls.Border border, string kind)
    {
        if (border.Child is Microsoft.UI.Xaml.Controls.Grid g && g.Tag as string == "VideoGlassWrap") return;
        var content = border.Child;
        border.Child = null;
        var pad = border.Padding;
        var glass = new Microsoft.UI.Xaml.Controls.Border
        {
            Margin = new Thickness(-pad.Left, -pad.Top, -pad.Right, -pad.Bottom),
            IsHitTestVisible = false
        };
        SetCornerRadius(glass, Math.Max(0, border.CornerRadius.TopLeft - border.BorderThickness.Left));
        SetKind(glass, kind);
        var grid = new Microsoft.UI.Xaml.Controls.Grid { Tag = "VideoGlassWrap" };
        grid.Children.Add(glass);
        if (content != null) grid.Children.Add(content);
        border.Child = grid;
    }

    /// <summary>Same as <see cref="Wrap(Microsoft.UI.Xaml.Controls.Border, string)"/> for a flyout's content.</summary>
    public static void Wrap(Microsoft.UI.Xaml.Controls.Flyout flyout, double cornerRadius)
    {
        if (flyout.Content is Microsoft.UI.Xaml.Controls.Grid g && g.Tag as string == "VideoGlassWrap") return;
        // A windowed popup lives outside the video's visual tree, so the glass couldn't line up with it
        flyout.ShouldConstrainToRootBounds = true;
        var content = flyout.Content;
        flyout.Content = null;
        var glass = new Microsoft.UI.Xaml.Controls.Border { IsHitTestVisible = false };
        SetCornerRadius(glass, cornerRadius);
        SetKind(glass, "Flyout");
        var grid = new Microsoft.UI.Xaml.Controls.Grid { Tag = "VideoGlassWrap" };
        grid.Children.Add(glass);
        if (content != null) grid.Children.Add(content);
        flyout.Content = grid;
    }

    /// <summary>Binds the swap chain LibVLCSharp hands to libvlc ("--winrt-swapchain=0x...").</summary>
    public void SetSwapChain(IEnumerable<string>? swapChainOptions)
    {
        IntPtr swapChain = IntPtr.Zero;
        if (swapChainOptions != null)
        {
            foreach (var option in swapChainOptions)
            {
                const string prefix = "--winrt-swapchain=0x";
                if (option.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(option.AsSpan(prefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value))
                {
                    swapChain = new IntPtr(value);
                }
            }
        }

        _mirror?.Dispose();
        _mirror = null;
        if (swapChain != IntPtr.Zero)
        {
            try
            {
                _factory ??= CreateFactory();
                _mirror = VideoFrameMirror.Create(swapChain, _compositor);
            }
            catch (Exception ex)
            {
                Log.Error("VideoGlass.SetSwapChain", ex);
                _mirror?.Dispose();
                _mirror = null;
            }
        }

        foreach (var pane in _panes)
        {
            Build(pane);
        }
        UpdateTimer();
    }

    /// <summary>Stops copying frames while the player is hidden.</summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        bool run = _isActive && _mirror != null && _panes.Count > 0 && Backdrop.IsEnabled;
        if (run)
        {
            if (_frameTimer == null)
            {
                _frameTimer = _video.DispatcherQueue.CreateTimer();
                // ~30 fps is plenty for a blurred backdrop and halves the copy work on 60 fps clips
                _frameTimer.Interval = TimeSpan.FromMilliseconds(33);
                _frameTimer.Tick += OnFrameTick;
            }
            if (!_frameTimer.IsRunning) _frameTimer.Start();
        }
        else
        {
            _frameTimer?.Stop();
        }
    }

    private void OnFrameTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_mirror == null) return;
        // Nothing to copy while every glass layer is hidden (controls auto-hidden, no flyout open)
        bool anyShown = false;
        foreach (var pane in _panes)
        {
            if (IsShown(pane.Host)) { anyShown = true; break; }
        }
        if (!anyShown) return;

        bool hadSurface = _mirror.Surface != null;
        if (_mirror.Update() && !hadSurface)
        {
            // First frame: the drawing surface exists now, so the panes can be built
            foreach (var pane in _panes) Build(pane);
        }
    }

    private static bool IsShown(FrameworkElement element)
    {
        if (!element.IsLoaded || element.ActualWidth <= 0) return false;
        DependencyObject? current = element;
        while (current is UIElement ui)
        {
            if (ui.Visibility != Visibility.Visible || ui.Opacity <= 0.01) return false;
            current = VisualTreeHelper.GetParent(current);
        }
        return true;
    }

    private CompositionEffectFactory CreateFactory()
    {
        var blur = new GaussianBlurEffect
        {
            Name = "Blur",
            BlurAmount = 30f,
            BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Speed,
            Source = new CompositionEffectSourceParameter("Video")
        };
        // A touch of extra saturation keeps the glass from looking muddy, like the system acrylic
        var saturation = new SaturationEffect { Saturation = 1.25f, Source = blur };
        var tint = new ColorSourceEffect { Name = "Tint", Color = Color.FromArgb(160, 18, 18, 18) };
        var composite = new CompositeEffect { Mode = Microsoft.Graphics.Canvas.CanvasComposite.SourceOver };
        composite.Sources.Add(saturation);
        composite.Sources.Add(tint);
        return _compositor.CreateEffectFactory(composite, new[] { "Blur.BlurAmount", "Tint.Color" });
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host) return;
        host.Loaded -= OnHostLoaded;
        host.Unloaded -= OnHostUnloaded;
        if (e.NewValue is string kind && kind.Length > 0)
        {
            host.Loaded += OnHostLoaded;
            host.Unloaded += OnHostUnloaded;
            if (host.IsLoaded) Current?.Register(host);
        }
        else
        {
            Current?.Unregister(host);
        }
    }

    private static void OnHostLoaded(object sender, RoutedEventArgs e) => Current?.Register((FrameworkElement)sender);
    private static void OnHostUnloaded(object sender, RoutedEventArgs e) => Current?.Unregister((FrameworkElement)sender);

    private void Register(FrameworkElement host)
    {
        if (_panes.Exists(p => p.Host == host)) return;
        var pane = new Pane
        {
            Host = host,
            Surface = string.Equals(GetKind(host), "Flyout", StringComparison.OrdinalIgnoreCase) ? Backdrop.Surface.Flyout : Backdrop.Surface.Bar
        };
        _panes.Add(pane);
        host.SizeChanged += OnHostSizeChanged;
        Build(pane);
        UpdateTimer();
        // Flyouts open with a slide, so follow them for the length of the open animation
        Track(450);
    }

    private void Unregister(FrameworkElement host)
    {
        var pane = _panes.Find(p => p.Host == host);
        if (pane == null) return;
        _panes.Remove(pane);
        host.SizeChanged -= OnHostSizeChanged;
        ElementCompositionPreview.SetElementChildVisual(host, null);
        pane.Brush?.Dispose();
        pane.Sprite?.Dispose();
        pane.Container?.Dispose();
        UpdateTimer();
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => SyncAll();

    private void Build(Pane pane)
    {
        if (!IsAvailable || !Backdrop.IsEnabled)
        {
            ElementCompositionPreview.SetElementChildVisual(pane.Host, null);
            pane.Container = null;
            pane.Sprite = null;
            pane.Brush = null;
            return;
        }

        var surfaceBrush = _compositor.CreateSurfaceBrush(_mirror!.Surface);
        surfaceBrush.Stretch = CompositionStretch.Fill;
        surfaceBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;

        pane.Brush = _factory!.CreateBrush();
        pane.Brush.SetSourceParameter("Video", surfaceBrush);
        ApplyLook(pane);

        pane.Sprite = _compositor.CreateSpriteVisual();
        pane.Sprite.Brush = pane.Brush;

        pane.Container = _compositor.CreateContainerVisual();
        pane.Container.Children.InsertAtTop(pane.Sprite);
        pane.ClipGeometry = _compositor.CreateRoundedRectangleGeometry();
        pane.Container.Clip = _compositor.CreateGeometricClip(pane.ClipGeometry);

        ElementCompositionPreview.SetElementChildVisual(pane.Host, pane.Container);
        Sync(pane);
    }

    private static void ApplyLook(Pane pane)
    {
        if (pane.Brush == null) return;
        var (tintOpacity, _) = Backdrop.GetTint(pane.Surface);
        byte baseShade = pane.Surface == Backdrop.Surface.Flyout ? (byte)26 : (byte)18;
        pane.Brush.Properties.InsertScalar("Blur.BlurAmount", Backdrop.GetBlurAmount());
        // Tint opacity comes from the strength setting, lifted a little so white text stays readable
        byte alpha = (byte)Math.Clamp((tintOpacity + 0.25) * 255, 0, 235);
        pane.Brush.Properties.InsertColor("Tint.Color", Color.FromArgb(alpha, baseShade, baseShade, baseShade));
    }

    /// <summary>Re-reads the blur setting (toggle and strength) for every pane.</summary>
    public void Refresh()
    {
        foreach (var pane in _panes)
        {
            if (pane.Brush == null || !Backdrop.IsEnabled)
            {
                Build(pane);
            }
            else
            {
                ApplyLook(pane);
            }
        }
        UpdateTimer();
    }

    /// <summary>Keeps the glass aligned with the video while a host moves through a render-transform animation.</summary>
    public void Track(int milliseconds)
    {
        _trackUntil = Math.Max(_trackUntil, Environment.TickCount64 + milliseconds);
        if (_isTracking || _panes.Count == 0) return;
        _isTracking = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        SyncAll();
        if (Environment.TickCount64 > _trackUntil)
        {
            CompositionTarget.Rendering -= OnRendering;
            _isTracking = false;
        }
    }

    private void SyncAll()
    {
        foreach (var pane in _panes)
        {
            Sync(pane);
        }
    }

    private void Sync(Pane pane)
    {
        if (pane.Container == null || pane.Sprite == null || pane.ClipGeometry == null) return;
        if (pane.Host.XamlRoot == null || _video.XamlRoot == null) return;
        try
        {
            var origin = pane.Host.TransformToVisual(_video).TransformPoint(new Windows.Foundation.Point(0, 0));
            var hostSize = new System.Numerics.Vector2((float)pane.Host.ActualWidth, (float)pane.Host.ActualHeight);
            pane.Container.Size = hostSize;
            pane.ClipGeometry.Size = hostSize;
            float radius = (float)GetCornerRadius(pane.Host);
            pane.ClipGeometry.CornerRadius = new System.Numerics.Vector2(radius, radius);
            // The sprite spans the whole video and is shifted so the glass lines up with what is behind it
            pane.Sprite.Size = new System.Numerics.Vector2((float)_video.ActualWidth, (float)_video.ActualHeight);
            pane.Sprite.Offset = new System.Numerics.Vector3((float)-origin.X, (float)-origin.Y, 0);
        }
        catch (ArgumentException)
        {
            // Host is in a popup that isn't connected to the video's tree yet
        }
    }
}
