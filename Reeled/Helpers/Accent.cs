using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Reeled.Helpers;

/// <summary>
/// The app's accent colour: the Windows accent, one of the presets, or any colour the user picks.
///
/// WinUI resolves accent brushes (AccentFillColorDefaultBrush and the control brushes that alias it)
/// once, from the SystemAccentColor shades. To recolour live, every brush in the merged theme
/// dictionaries whose colour is one of the Windows accent shades is remembered on first use and then
/// repainted in place, so buttons, toggles, sliders and selection indicators all change at once
/// without reloading pages.
/// </summary>
public static class Accent
{
    public const string SystemMode = "System";

    /// <summary>Hand-picked presets that read well on both the dark and the light theme.</summary>
    public static readonly (string Hex, string NameKey)[] Presets =
    {
        ("#F7820F", "Accent_Orange"),
        ("#FFB900", "Accent_Amber"),
        ("#E74856", "Accent_Red"),
        ("#E3008C", "Accent_Pink"),
        ("#8E62D9", "Accent_Violet"),
        ("#0078D4", "Accent_Blue"),
        ("#00A8C6", "Accent_Teal"),
        ("#13A10E", "Accent_Green"),
        ("#7A7574", "Accent_Graphite"),
    };

    private static readonly string[] ShadeKeys =
    {
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    };

    private static readonly Windows.UI.ViewManagement.UISettings UiSettings = new();
    private static List<(SolidColorBrush Brush, int Shade)>? _accentBrushes;
    private static Color[]? _applied;

    public static event EventHandler? Changed;

    /// <summary>The colour currently in use (base shade).</summary>
    public static Color Current => _applied?[0] ?? SystemShades()[0];

    /// <summary>Applies the accent stored in settings. Safe to call before any UI exists.</summary>
    public static void ApplyFromSettings()
    {
        string? setting = null;
        try { setting = App.GetService<Services.ILocalStorageService>().CurrentSettings.AccentColor; } catch { }
        Apply(setting);
    }

    /// <summary>"System" (or null) follows Windows; otherwise a #RRGGBB colour.</summary>
    public static void Apply(string? setting)
    {
        Color[] shades = TryParse(setting, out var color) ? ShadesFor(color) : SystemShades();
        Apply(shades);
    }

    /// <summary>Applies a colour directly, without a hex round trip (live preview while dragging a picker).</summary>
    public static void Apply(Color color) => Apply(ShadesFor(color));

    private static void Apply(Color[] shades)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;

        // Nothing to repaint when the shades are the ones already in use
        if (_applied != null)
        {
            bool same = true;
            for (int i = 0; i < shades.Length && same; i++) same = _applied[i] == shades[i];
            if (same) return;
        }

        // Capture the brushes painted with the Windows shades once, before anything is repainted
        _accentBrushes ??= FindAccentBrushes(resources, SystemShades());

        for (int i = 0; i < ShadeKeys.Length; i++)
        {
            resources[ShadeKeys[i]] = shades[i];
        }

        foreach (var (brush, shade) in _accentBrushes)
        {
            try
            {
                var c = shades[shade];
                brush.Color = Color.FromArgb(brush.Color.A, c.R, c.G, c.B);
            }
            catch { }
        }

        _applied = shades;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Re-applies when Windows' accent changes and the app follows it.</summary>
    public static void OnSystemColorsChanged()
    {
        string? setting = null;
        try { setting = App.GetService<Services.ILocalStorageService>().CurrentSettings.AccentColor; } catch { }
        if (!TryParse(setting, out _)) Apply(SystemShades());
    }

    public static Color SystemColor => SystemShades()[0];

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex) || hex == SystemMode) return false;
        string h = hex.Trim().TrimStart('#');
        if (h.Length != 6) return false;
        try
        {
            color = Color.FromArgb(255,
                Convert.ToByte(h.Substring(0, 2), 16),
                Convert.ToByte(h.Substring(2, 2), 16),
                Convert.ToByte(h.Substring(4, 2), 16));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color[] SystemShades()
    {
        try
        {
            return new[]
            {
                UiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent),
                UiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight1),
                UiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight2),
                UiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight3),
                UiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark1),
                UiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark2),
                UiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark3),
            };
        }
        catch
        {
            return ShadesFor(Color.FromArgb(255, 0, 120, 212));
        }
    }

    /// <summary>
    /// Light and dark shades in the spirit of the Windows palette: lighter shades move towards white
    /// (used on dark backgrounds with dark text on top), darker ones towards black (light theme, white
    /// text on top). Lightness is clamped so text on accent stays readable whatever colour is picked.
    /// </summary>
    public static Color[] ShadesFor(Color c)
    {
        RgbToHsl(c, out double h, out double s, out double l);
        Color Shade(double lightness, double satBoost = 0) =>
            HslToRgb(h, Math.Clamp(s + satBoost, 0, 1), Math.Clamp(lightness, 0, 1));

        return new[]
        {
            c,
            Shade(Math.Max(l + (1 - l) * 0.14, 0.46)),
            Shade(Math.Max(l + (1 - l) * 0.44, 0.62), 0.04),
            Shade(Math.Max(l + (1 - l) * 0.64, 0.76), 0.04),
            Shade(Math.Min(l * 0.86, 0.42)),
            Shade(Math.Min(l * 0.68, 0.33)),
            Shade(Math.Min(l * 0.48, 0.24)),
        };
    }

    private static List<(SolidColorBrush, int)> FindAccentBrushes(ResourceDictionary root, Color[] shades)
    {
        var found = new List<(SolidColorBrush, int)>();
        var seen = new HashSet<SolidColorBrush>();

        void Scan(ResourceDictionary dict, int depth)
        {
            if (depth > 6) return;
            try
            {
                foreach (var pair in dict)
                {
                    if (pair.Value is SolidColorBrush brush && seen.Add(brush))
                    {
                        int shade = ShadeIndex(brush.Color, shades);
                        if (shade >= 0) found.Add((brush, shade));
                    }
                }
            }
            catch { }
            try
            {
                foreach (var theme in dict.ThemeDictionaries.Values)
                {
                    if (theme is ResourceDictionary td) Scan(td, depth + 1);
                }
            }
            catch { }
            try
            {
                foreach (var merged in dict.MergedDictionaries) Scan(merged, depth + 1);
            }
            catch { }
        }

        Scan(root, 0);
        return found;
    }

    private static int ShadeIndex(Color c, Color[] shades)
    {
        for (int i = 0; i < shades.Length; i++)
        {
            if (shades[i].R == c.R && shades[i].G == c.G && shades[i].B == c.B) return i;
        }
        return -1;
    }

    /// <summary>Relative luminance (0..1), for picking readable text on a swatch.</summary>
    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        if (max == min)
        {
            h = s = 0;
            return;
        }
        double d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6;
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        return Color.FromArgb(255, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
