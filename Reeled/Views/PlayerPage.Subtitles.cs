using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Models;

namespace Reeled.Views;

/// <summary>
/// Subtitle files the player draws itself: in sync with playback (and the subtitle delay), styled
/// live from the session panel, and lifted above the controls while they're showing.
/// </summary>
public sealed partial class PlayerPage
{
    private string? _shownSubtitle;
    private DropShadow? _subtitleShadow;

    private void UpdateSubtitleOverlay(TimeSpan time)
    {
        var cues = ViewModel.ExternalSubtitleCues;
        string? text = null;
        if (ViewModel.IsExternalSubtitleActive && cues != null)
        {
            var t = time - TimeSpan.FromMilliseconds(ViewModel.PlaybackService.SubtitleDelayMs);
            text = Helpers.SubtitleParser.TextAt(cues, t);
        }
        if (text == _shownSubtitle) return;
        _shownSubtitle = text;
        if (string.IsNullOrEmpty(text))
        {
            SubtitleOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        SubtitleOverlayText.Text = text;
        SubtitleOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>Size follows the video height, like libvlc's own renderer.</summary>
    private void ApplySubtitleOverlayStyle()
    {
        if (SubtitleOverlay == null) return;
        var style = ViewModel.PlaybackService.SubtitleStyle ?? new SubtitleStyle();
        double height = RootContainer.ActualHeight > 0 ? RootContainer.ActualHeight : 720;
        SubtitleOverlayText.FontSize = Math.Clamp(height * 0.047 * style.Scale, 14, 120);
        SubtitleOverlayText.FontWeight = style.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold;
        SubtitleOverlayText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255,
            (byte)((style.ColorRgb >> 16) & 0xFF), (byte)((style.ColorRgb >> 8) & 0xFF), (byte)(style.ColorRgb & 0xFF)));
        SubtitleOverlay.Background = style.Background == SubtitleBackground.Box
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(180, 0, 0, 0))
            : null;

        EnsureSubtitleShadow();
        if (_subtitleShadow != null)
        {
            // A tight, dark shadow reads as an outline; off for the box and "none" styles
            _subtitleShadow.Opacity = style.Background == SubtitleBackground.Outline ? 1f : 0f;
        }
    }

    private void EnsureSubtitleShadow()
    {
        if (_subtitleShadow != null || SubtitleOverlayText.Parent is not Microsoft.UI.Xaml.Controls.Grid host) return;
        try
        {
            var layer = new Microsoft.UI.Xaml.Shapes.Rectangle { IsHitTestVisible = false };
            host.Children.Insert(0, layer);
            var compositor = ElementCompositionPreview.GetElementVisual(layer).Compositor;
            _subtitleShadow = compositor.CreateDropShadow();
            _subtitleShadow.BlurRadius = 4f;
            _subtitleShadow.Color = Microsoft.UI.Colors.Black;
            _subtitleShadow.Offset = new Vector3(0, 1, 0);
            _subtitleShadow.Mask = SubtitleOverlayText.GetAlphaMask();
            var sprite = compositor.CreateSpriteVisual();
            sprite.Shadow = _subtitleShadow;
            var size = compositor.CreateExpressionAnimation("host.Size");
            size.SetReferenceParameter("host", ElementCompositionPreview.GetElementVisual(layer));
            sprite.StartAnimation("Size", size);
            ElementCompositionPreview.SetElementChildVisual(layer, sprite);
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("SubtitleShadow", ex);
        }
    }

    /// <summary>Keeps subtitles clear of the bottom bar while it's showing (the bar's measured height, so Classic and Compact both get the same gap).</summary>
    private void AnimateSubtitleLift(bool controlsVisible, bool animate)
    {
        if (SubtitleOverlayTranslation == null) return;
        double lift = controlsVisible ? -Math.Max(0, BarClearance - SubtitleBaseMargin) : 0;
        if (!animate || Helpers.Motion.IsReduced)
        {
            SubtitleOverlayTranslation.Y = lift;
            return;
        }
        var sb = new Storyboard();
        sb.Children.Add(Helpers.Motion.Spline(SubtitleOverlayTranslation, "Y", lift, controlsVisible ? Helpers.Motion.Medium : Helpers.Motion.Long));
        Helpers.Motion.BeginExact(sb);
    }
}
