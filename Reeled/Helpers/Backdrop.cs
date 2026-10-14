using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Reeled.Helpers;

/// <summary>
/// Acrylic settings shared by the player chrome and flyouts, driven by the "Backdrop blur" toggle and
/// the blur strength choice in Settings. Lower tint lets more of the blurred video show through.
/// </summary>
public static class Backdrop
{
    public enum Surface
    {
        /// <summary>Flyouts, popups and badges floating over the video.</summary>
        Flyout,
        /// <summary>Full-width player bars and the queue drawer.</summary>
        Bar
    }

    private static Services.ILocalStorageService Storage => App.GetService<Services.ILocalStorageService>();

    public static bool IsEnabled => Storage.CurrentSettings.EnableBackdropBlur;

    /// <summary>0 = Light, 1 = Balanced, 2 = Strong.</summary>
    public static int Level => System.Math.Clamp(Storage.CurrentSettings.BackdropBlurLevel, 0, 2);

    public static (double TintOpacity, double LuminosityOpacity) GetTint(Surface surface) => (surface, Level) switch
    {
        // Luminosity opacity keeps white text readable over bright footage; tint opacity sets
        // how much of the blurred video colour comes through
        (Surface.Flyout, 0) => (0.28, 0.55),
        (Surface.Flyout, 2) => (0.65, 0.85),
        (Surface.Flyout, _) => (0.45, 0.70),
        (Surface.Bar, 0) => (0.20, 0.50),
        (Surface.Bar, 2) => (0.55, 0.80),
        (_, _) => (0.35, 0.62)
    };

    /// <summary>Updates an existing acrylic brush in place so every element using it follows the setting.</summary>
    public static void Apply(AcrylicBrush brush, Color tintColor, Surface surface)
    {
        var (tint, luminosity) = GetTint(surface);
        brush.TintColor = tintColor;
        brush.TintOpacity = tint;
        brush.TintLuminosityOpacity = luminosity;
        brush.AlwaysUseFallback = !IsEnabled;
        // Without blur the surface must stay readable over bright video
        brush.FallbackColor = Color.FromArgb(IsEnabled ? (byte)0xD9 : (byte)0xF5, tintColor.R, tintColor.G, tintColor.B);
    }

    public static AcrylicBrush Create(Color tintColor, Surface surface)
    {
        var brush = new AcrylicBrush();
        Apply(brush, tintColor, surface);
        return brush;
    }
}
