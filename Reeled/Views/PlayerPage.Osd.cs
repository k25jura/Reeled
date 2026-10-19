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
    private Storyboard? _osdStoryboard;
    private DispatcherTimer? _osdHideTimer;
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

        _osdHideTimer?.Stop();
        _osdStoryboard?.Stop();
        var sb = new Storyboard();
        AnimateOsdTint(sb, tint, immediate: firstShow);

        if (firstShow)
        {
            OsdToastContainer.Visibility = Visibility.Visible;
            sb.Children.Add(Helpers.Motion.Spline(OsdToastContainer, "Opacity", 1, Helpers.Motion.Short, from: OsdToastContainer.Opacity));
            double rise = prefs.Position == OsdPosition.Bottom ? -10 : 10;
            sb.Children.Add(Helpers.Motion.Spline(OsdContentTranslation, "Y", 0, Helpers.Motion.Medium, from: -rise));
        }
        else
        {
            // Already up: hold it and give the new text a tiny nudge so each step registers
            OsdToastContainer.Opacity = 1;
            sb.Children.Add(Helpers.Motion.Spline(OsdContentTranslation, "Y", 0, Helpers.Motion.Short, from: prefs.Position == OsdPosition.Bottom ? 3 : -3));
        }
        _osdStoryboard = sb;
        Helpers.Motion.BeginExact(sb);
        StartOsdHideTimer(prefs.DurationMs);
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

    private void StartOsdHideTimer(int durationMs)
    {
        _osdHideTimer?.Stop();
        _osdHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Clamp(durationMs, 400, 5000)) };
        _osdHideTimer.Tick += (s, e) =>
        {
            _osdHideTimer?.Stop();
            var sbOut = new Storyboard();
            sbOut.Children.Add(Helpers.Motion.Spline(OsdToastContainer, "Opacity", 0, Helpers.Motion.Medium, accelerate: true));
            sbOut.Completed += (s2, e2) =>
            {
                if (!ReferenceEquals(_osdStoryboard, sbOut)) return;
                _osdStoryboard = null;
                OsdToastContainer.Visibility = Visibility.Collapsed;
            };
            _osdStoryboard = sbOut;
            Helpers.Motion.BeginExact(sbOut);
        };
        _osdHideTimer.Start();
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
                OsdContent.Margin = new Thickness(0, 0, 0, 120);
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
