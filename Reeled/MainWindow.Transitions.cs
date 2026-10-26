using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Helpers;

namespace Reeled;

/// <summary>
/// Motion between the app's three places. The player rises over the library, which recedes and dims behind
/// it, and sinks back on close. The player itself only slides: scaling it would rescale the video's swap
/// chain, which libvlc then resizes. Library and Settings swap with a fade-through: pages are cached and
/// leave the frame when it navigates, so there is no old page left to crossfade with; the current page
/// recedes quickly, then the next one settles into place.
/// </summary>
public sealed partial class MainWindow
{
    private const double PlayerRestOffset = 28;
    private const double LibraryRecessScale = 0.97;

    private ScaleTransform? _frameScale;
    private TranslateTransform? _playerShift;
    private Storyboard? _pageStoryboard;
    private int _pageTransitionToken;

    private ScaleTransform FrameScale => _frameScale ??= AttachScale(RootFrame);
    private TranslateTransform PlayerShift => _playerShift ??= (TranslateTransform)(PlayerOverlayContainer.RenderTransform = new TranslateTransform());

    private static ScaleTransform AttachScale(UIElement element)
    {
        var scale = new ScaleTransform();
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        element.RenderTransform = scale;
        return scale;
    }

    private static void AddScale(Storyboard sb, ScaleTransform scale, double to, double ms, double? from = null, bool accelerate = false, double delayMs = 0)
    {
        sb.Children.Add(Motion.Spline(scale, "ScaleX", to, ms, from, delayMs, accelerate));
        sb.Children.Add(Motion.Spline(scale, "ScaleY", to, ms, from, delayMs, accelerate));
    }

    private static void SetScale(ScaleTransform scale, double value)
    {
        scale.ScaleX = value;
        scale.ScaleY = value;
    }

    /// <summary>
    /// Stops the running player transition where it is: animated values outrank local ones, so they are read
    /// first and written back, and the next transition starts from what is on screen instead of jumping.
    /// </summary>
    private void FreezePlayerTransition()
    {
        if (_transitionStoryboard == null) return;
        double player = PlayerOverlayContainer.Opacity, backdrop = PlayerBackdropLayer.Opacity, frame = RootFrame.Opacity;
        double title = AppTitleBar.Opacity, playerShift = PlayerShift.Y, frameScale = FrameScale.ScaleX;
        _transitionStoryboard.Stop();
        _transitionStoryboard = null;
        PlayerOverlayContainer.Opacity = player;
        PlayerBackdropLayer.Opacity = backdrop;
        RootFrame.Opacity = frame;
        AppTitleBar.Opacity = title;
        PlayerShift.Y = playerShift;
        SetScale(FrameScale, frameScale);
    }

    private void AnimatePlayerOpen()
    {
        // A page change still running would fight over the frame's opacity
        ++_pageTransitionToken;
        _pageStoryboard?.Stop();
        _pageStoryboard = null;
        FreezePlayerTransition();
        bool fromRest = PlayerOverlayContainer.Opacity < 0.01;
        if (fromRest) PlayerShift.Y = PlayerRestOffset;

        var sb = new Storyboard();
        sb.Children.Add(Motion.Spline(PlayerOverlayContainer, "Opacity", 1, Motion.Medium, from: PlayerOverlayContainer.Opacity));
        sb.Children.Add(Motion.Spline(PlayerShift, "Y", 0, Motion.Long, from: PlayerShift.Y));
        sb.Children.Add(Motion.Spline(PlayerBackdropLayer, "Opacity", 1, Motion.Short, from: PlayerBackdropLayer.Opacity));
        sb.Children.Add(Motion.Spline(RootFrame, "Opacity", 0, Motion.Long, from: RootFrame.Opacity));
        AddScale(sb, FrameScale, LibraryRecessScale, Motion.Long, from: FrameScale.ScaleX);
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_transitionStoryboard, sb)) return;
            sb.Stop();
            _transitionStoryboard = null;
            PlayerOverlayContainer.Opacity = 1.0;
            PlayerBackdropLayer.Opacity = 1.0;
            RootFrame.Opacity = 0.0;
            PlayerShift.Y = 0;
            // Hidden now; at rest for the next time it shows
            SetScale(FrameScale, 1);
        };
        _transitionStoryboard = sb;
        Motion.BeginExact(sb);
    }

    private void AnimatePlayerClose(Action completed)
    {
        bool wasOpen = _transitionStoryboard == null && RootFrame.Opacity < 0.01;
        FreezePlayerTransition();
        // The library was set back to rest once the player covered it; it rises from where it receded to
        if (wasOpen) SetScale(FrameScale, LibraryRecessScale);

        var sb = new Storyboard();
        sb.Children.Add(Motion.Spline(PlayerOverlayContainer, "Opacity", 0, Motion.Short, from: PlayerOverlayContainer.Opacity, accelerate: true));
        sb.Children.Add(Motion.Spline(PlayerShift, "Y", 18, Motion.Short, from: PlayerShift.Y, accelerate: true));
        sb.Children.Add(Motion.Spline(PlayerBackdropLayer, "Opacity", 0, Motion.Medium, from: PlayerBackdropLayer.Opacity));
        sb.Children.Add(Motion.Spline(RootFrame, "Opacity", 1, Motion.Medium, from: RootFrame.Opacity, delayMs: 40));
        AddScale(sb, FrameScale, 1, Motion.Long, from: FrameScale.ScaleX);
        sb.Children.Add(Motion.Spline(AppTitleBar, "Opacity", 1, Motion.Medium, from: AppTitleBar.Opacity));
        sb.Completed += (s, e) =>
        {
            if (!ReferenceEquals(_transitionStoryboard, sb)) return;
            sb.Stop();
            _transitionStoryboard = null;
            PlayerShift.Y = 0;
            SetScale(FrameScale, 1);
            completed();
        };
        _transitionStoryboard = sb;
        Motion.BeginExact(sb);
    }

    /// <summary>
    /// Library ⇄ Settings. <paramref name="deeper"/> (into Settings): the library swells slightly as it fades
    /// and Settings rises from a little smaller; back is the mirror image.
    /// </summary>
    private void NavigateFrameWithMotion(Action navigate, bool deeper)
    {
        int token = ++_pageTransitionToken;
        _pageStoryboard?.Stop();
        _pageStoryboard = null;
        SetScale(FrameScale, 1);

        // Nothing on screen to move away from (first page, or the player covered the frame)
        if (Motion.IsReduced || RootFrame.Content == null || RootFrame.Opacity < 0.01)
        {
            RootFrame.Opacity = 1.0;
            navigate();
            if (!Motion.IsReduced && RootFrame.Content != null) PageIn(token, deeper);
            return;
        }

        var outSb = new Storyboard();
        outSb.Children.Add(Motion.Spline(RootFrame, "Opacity", 0, 110, from: RootFrame.Opacity, accelerate: true));
        AddScale(outSb, FrameScale, deeper ? 1.02 : 0.98, 110, from: 1, accelerate: true);
        outSb.Completed += (s, e) =>
        {
            if (token != _pageTransitionToken) return;
            outSb.Stop();
            RootFrame.Opacity = 0.0;
            navigate();
            PageIn(token, deeper);
        };
        _pageStoryboard = outSb;
        Motion.BeginExact(outSb);
    }

    private void PageIn(int token, bool deeper)
    {
        RootFrame.Opacity = 0.0;
        var inSb = new Storyboard();
        inSb.Children.Add(Motion.Spline(RootFrame, "Opacity", 1, Motion.Medium, from: 0, delayMs: 20));
        AddScale(inSb, FrameScale, 1, Motion.Long, from: deeper ? 0.97 : 1.03);
        inSb.Completed += (s, e) =>
        {
            if (token != _pageTransitionToken) return;
            inSb.Stop();
            _pageStoryboard = null;
            RootFrame.Opacity = 1.0;
            SetScale(FrameScale, 1);
        };
        _pageStoryboard = inSb;
        Motion.BeginExact(inSb);
    }
}
