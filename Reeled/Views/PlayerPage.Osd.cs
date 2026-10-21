using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;
using Reeled.ViewModels;
using Windows.UI;

namespace Reeled.Views;

/// <summary>
/// On-screen display: large text with a soft shadow over a gradient band. The band takes a dark tint
/// from the kind of action (blue for seeking, teal for volume, amber for boost, violet for speed, gold
/// for moments...) and cross-fades between tints, so rapid changes read as one continuous message.
/// </summary>
public sealed partial class PlayerPage
{
    private Storyboard? _osdShowStoryboard;
    private Storyboard? _osdHideStoryboard;
    private DispatcherTimer? _osdHideTimer;
    private DispatcherTimer? _osdSafetyTimer;
    // Bumped by every new toast and by HideOsdNow; a late timer tick or Completed handler from an older
    // toast sees a different value and does nothing
    private int _osdGeneration;
    private bool _osdShadowReady;
    private OsdPosition _osdAppliedPosition = (OsdPosition)(-1);

    private static Color OsdTint(OsdKind kind) => kind switch
    {
        OsdKind.Seek => Color.FromArgb(255, 10, 24, 44),
        OsdKind.Volume => Color.FromArgb(255, 8, 34, 34),
        OsdKind.VolumeBoost => Color.FromArgb(255, 46, 26, 6),
        OsdKind.Speed => Color.FromArgb(255, 30, 16, 46),
        OsdKind.Moment => Color.FromArgb(255, 44, 34, 6),
        OsdKind.Tracks => Color.FromArgb(255, 14, 30, 22),
        OsdKind.Mode => Color.FromArgb(255, 24, 24, 38),
        OsdKind.Stop => Color.FromArgb(255, 46, 12, 14),
        _ => Color.FromArgb(255, 12, 14, 18)
    };

    private void UpdateOsdToast(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateOsdToast(message));
            return;
        }
        var prefs = Storage.CurrentSettings.Osd ?? new OsdPreferences();

        ApplyOsdLayout(prefs);
        EnsureOsdShadow();

        OsdToastText.Text = message;
        OsdToastText.FontSize = Math.Clamp(prefs.FontSize, 24, 96);
        string? detail = prefs.ShowDetails ? ViewModel.OsdDetail : null;
        OsdDetailText.Text = detail ?? string.Empty;
        OsdDetailText.FontSize = Math.Clamp(prefs.FontSize * 0.36, 12, 32);
        OsdDetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;

        OsdGradient.Visibility = prefs.ShowBackdrop ? Visibility.Visible : Visibility.Collapsed;
        var tint = prefs.TintByAction ? OsdTint(ViewModel.OsdKind) : OsdTint(OsdKind.Neutral);
        bool firstShow = OsdToastContainer.Visibility != Visibility.Visible || OsdToastContainer.Opacity < 0.05;

        // Drop everything the previous toast left running. A finished storyboard keeps holding its end value
        // (e.g. the fade-out's 0) underneath newer ones, so each one is stopped and the state set directly.
        int generation = CancelOsdAnimations();
        var sb = new Storyboard();
        AnimateOsdTint(sb, tint, immediate: firstShow);

        if (firstShow)
        {
            OsdToastContainer.Opacity = 0;
            OsdToastContainer.Visibility = Visibility.Visible;
            sb.Children.Add(Helpers.Motion.Spline(OsdToastContainer, "Opacity", 1, Helpers.Motion.Short, from: 0));
            double rise = prefs.Position == OsdPosition.Bottom ? -10 : 10;
            sb.Children.Add(Helpers.Motion.Spline(OsdContentTranslation, "Y", 0, Helpers.Motion.Medium, from: -rise));
        }
        else
        {
            // Already up: hold it and give the new text a tiny nudge so each step registers
            OsdToastContainer.Opacity = 1;
            sb.Children.Add(Helpers.Motion.Spline(OsdContentTranslation, "Y", 0, Helpers.Motion.Short, from: prefs.Position == OsdPosition.Bottom ? 3 : -3));
        }
        sb.Completed += (s, e) =>
        {
            if (generation != _osdGeneration) return;
            // Release the held values so a later fade-out starts from a clean slate
            sb.Stop();
            _osdShowStoryboard = null;
            OsdToastContainer.Opacity = 1;
            OsdContentTranslation.Y = 0;
        };
        _osdShowStoryboard = sb;
        // The hide timer comes first: whatever the animation does, this toast will go away
        StartOsdHideTimer(prefs.DurationMs, generation);
        try
        {
            Helpers.Motion.BeginExact(sb);
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("OsdShow", ex);
            OsdToastContainer.Opacity = 1;
        }
    }

    /// <summary>Stops the timers and storyboards of the toast on screen and returns the new generation.</summary>
    private int CancelOsdAnimations()
    {
        _osdGeneration++;
        _osdHideTimer?.Stop();
        _osdSafetyTimer?.Stop();
        try
        {
            _osdShowStoryboard?.Stop();
            _osdHideStoryboard?.Stop();
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("OsdCancel", ex);
        }
        _osdShowStoryboard = null;
        _osdHideStoryboard = null;
        return _osdGeneration;
    }

    /// <summary>Takes the toast off screen right now (leaving the player, clip changes that reset the view).</summary>
    private void HideOsdNow()
    {
        CancelOsdAnimations();
        OsdToastContainer.Opacity = 0;
        OsdToastContainer.Visibility = Visibility.Collapsed;
    }

    private void AnimateOsdTint(Storyboard sb, Color tint, bool immediate)
    {
        var start = Color.FromArgb(184, tint.R, tint.G, tint.B);
        var middle = Color.FromArgb(86, tint.R, tint.G, tint.B);
        if (immediate || Helpers.Motion.IsReduced)
        {
            OsdGradientStart.Color = start;
            OsdGradientMiddle.Color = middle;
            OsdGradientMirror.Color = middle;
            return;
        }
        foreach (var (stop, to) in new[] { (OsdGradientStart, start), (OsdGradientMiddle, middle), (OsdGradientMirror, middle) })
        {
            var anim = new ColorAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(Helpers.Motion.Medium)),
                EasingFunction = Helpers.Motion.Standard
            };
            Storyboard.SetTarget(anim, stop);
            Storyboard.SetTargetProperty(anim, "Color");
            sb.Children.Add(anim);
        }
    }

    private void StartOsdHideTimer(int durationMs, int generation)
    {
        _osdHideTimer?.Stop();
        _osdHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Clamp(durationMs, 400, 5000)) };
        _osdHideTimer.Tick += (s, e) =>
        {
            if (s is DispatcherTimer timer) timer.Stop();
            if (generation != _osdGeneration) return;

            _osdShowStoryboard?.Stop();
            _osdShowStoryboard = null;
            var sbOut = new Storyboard();
            sbOut.Children.Add(Helpers.Motion.Spline(OsdToastContainer, "Opacity", 0, Helpers.Motion.Medium, from: OsdToastContainer.Opacity, accelerate: true));
            sbOut.Completed += (s2, e2) => FinishOsdHide(generation);
            _osdHideStoryboard = sbOut;

            // Completed can be skipped (the storyboard gets replaced, the app is busy), so a second timer
            // guarantees the toast is collapsed shortly after the fade ends
            _osdSafetyTimer?.Stop();
            _osdSafetyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Helpers.Motion.Medium + 300) };
            _osdSafetyTimer.Tick += (s3, e3) =>
            {
                if (s3 is DispatcherTimer safety) safety.Stop();
                FinishOsdHide(generation);
            };
            _osdSafetyTimer.Start();

            try
            {
                Helpers.Motion.BeginExact(sbOut);
            }
            catch (Exception ex)
            {
                Helpers.Log.Error("OsdHide", ex);
                FinishOsdHide(generation);
            }
        };
        _osdHideTimer.Start();
    }

    private void FinishOsdHide(int generation)
    {
        if (generation != _osdGeneration) return;
        _osdSafetyTimer?.Stop();
        try { _osdHideStoryboard?.Stop(); } catch { }
        _osdHideStoryboard = null;
        OsdToastContainer.Opacity = 0;
        OsdToastContainer.Visibility = Visibility.Collapsed;
    }

    /// <summary>Top (under the header), centre, or bottom (over the controls); the band follows.</summary>
    private void ApplyOsdLayout(OsdPreferences prefs)
    {
        if (_osdAppliedPosition == prefs.Position) return;
        _osdAppliedPosition = prefs.Position;
        switch (prefs.Position)
        {
            case OsdPosition.Center:
                OsdContent.VerticalAlignment = VerticalAlignment.Center;
                OsdContent.Margin = new Thickness(0);
                OsdGradient.VerticalAlignment = VerticalAlignment.Center;
                OsdGradient.Height = 300;
                // Symmetric band: clear, tinted middle, clear
                SetOsdStops(endA: 0, mirror: 0.25, start: 0.5, middle: 0.75, endB: 1);
                break;
            case OsdPosition.Bottom:
                OsdContent.VerticalAlignment = VerticalAlignment.Bottom;
                OsdContent.Margin = new Thickness(0, 0, 0, BarClearance + 16);
                OsdGradient.VerticalAlignment = VerticalAlignment.Bottom;
                OsdGradient.Height = 280;
                SetOsdStops(endA: 0, mirror: 0.5, start: 1, middle: 0.5, endB: 0);
                break;
            default:
                OsdContent.VerticalAlignment = VerticalAlignment.Top;
                OsdContent.Margin = new Thickness(0, 58, 0, 0);
                OsdGradient.VerticalAlignment = VerticalAlignment.Top;
                OsdGradient.Height = 240;
                SetOsdStops(endA: 1, mirror: 0.5, start: 0, middle: 0.5, endB: 1);
                break;
        }
    }

    private void SetOsdStops(double endA, double mirror, double start, double middle, double endB)
    {
        OsdGradientEndA.Offset = endA;
        OsdGradientMirror.Offset = mirror;
        OsdGradientStart.Offset = start;
        OsdGradientMiddle.Offset = middle;
        OsdGradientEndB.Offset = endB;
    }

    /// <summary>A soft drop shadow under the OSD text (composition, from the text's own alpha mask).</summary>
    private void EnsureOsdShadow()
    {
        if (_osdShadowReady) return;
        _osdShadowReady = true;
        try
        {
            AttachTextShadow(OsdToastText, 14f, 0.75f);
            AttachTextShadow(OsdDetailText, 8f, 0.7f);
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("OsdShadow", ex);
        }
    }

    private static void AttachTextShadow(Microsoft.UI.Xaml.Controls.TextBlock text, float blur, float opacity)
    {
        var host = new Microsoft.UI.Xaml.Shapes.Rectangle { IsHitTestVisible = false };
        if (text.Parent is not Microsoft.UI.Xaml.Controls.Panel panel) return;

        // The shadow lives in a sibling sized to the text, behind it
        var wrapper = new Microsoft.UI.Xaml.Controls.Grid { HorizontalAlignment = text.HorizontalAlignment };
        int index = panel.Children.IndexOf(text);
        panel.Children.RemoveAt(index);
        wrapper.Children.Add(host);
        wrapper.Children.Add(text);
        panel.Children.Insert(index, wrapper);
        wrapper.Visibility = text.Visibility;
        text.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) => wrapper.Visibility = text.Visibility);

        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        var shadow = compositor.CreateDropShadow();
        shadow.BlurRadius = blur;
        shadow.Opacity = opacity;
        shadow.Color = Microsoft.UI.Colors.Black;
        shadow.Offset = new Vector3(0, 2, 0);
        shadow.Mask = text.GetAlphaMask();
        var sprite = compositor.CreateSpriteVisual();
        sprite.Shadow = shadow;
        var sizeBinding = compositor.CreateExpressionAnimation("host.Size");
        sizeBinding.SetReferenceParameter("host", ElementCompositionPreview.GetElementVisual(host));
        sprite.StartAnimation("Size", sizeBinding);
        ElementCompositionPreview.SetElementChildVisual(host, sprite);
    }
}
