using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Reeled.Helpers;

/// <summary>
/// Theme-correct brushes for code-behind. Application.Current.Resources[...] answers for the APPLICATION's
/// theme (the Windows setting), not the theme of the element that will use the brush, so a brush fetched
/// that way is white-on-white in the light theme whenever Windows itself is dark. This looks the key up in
/// the Light or Dark theme dictionary that matches the element, and <see cref="Track"/> re-runs the caller's
/// painting whenever that element's theme changes.
/// </summary>
public static class ThemeBrushes
{
    private static readonly Dictionary<(string Key, bool Light), Brush> Cache = new();

    /// <summary>The theme an element is actually rendered with.</summary>
    public static ElementTheme ThemeOf(FrameworkElement? element)
    {
        var theme = element?.ActualTheme ?? ElementTheme.Default;
        return theme == ElementTheme.Default ? App.CurrentTheme : theme;
    }

    public static Brush Get(string key, FrameworkElement? owner) => Get(key, ThemeOf(owner));

    public static Brush Get(string key, ElementTheme theme)
    {
        if (theme == ElementTheme.Default) theme = App.CurrentTheme;
        bool light = theme == ElementTheme.Light;
        if (Cache.TryGetValue((key, light), out var cached)) return cached;

        var resources = Application.Current?.Resources;
        if (resources != null)
        {
            object? found = Find(resources, key, light ? "Light" : "Dark", 0) ?? Find(resources, key, "Default", 0);
            if (found is Brush brush)
            {
                // Brushes are shared instances (the accent is repainted in place), so they are safe to keep
                Cache[(key, light)] = brush;
                return brush;
            }
            if (resources.TryGetValue(key, out var fallback) && fallback is Brush fb) return fb;
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    /// <summary>Sets a brush property now and keeps it right when the element enters the tree or its theme changes.</summary>
    public static void Bind(FrameworkElement element, DependencyProperty property, string key)
    {
        Track(element, () => element.SetValue(property, Get(key, element)));
    }

    /// <summary>
    /// Runs <paramref name="refresh"/> now, when the element is loaded (its real theme is known only once it
    /// is in the tree) and whenever its theme changes.
    /// </summary>
    public static void Track(FrameworkElement element, Action refresh)
    {
        refresh();
        element.Loaded += (s, e) => refresh();
        element.ActualThemeChanged += (s, e) => refresh();
    }

    /// <summary>One entry of the Light, Dark or Default theme dictionaries of the application, or null.</summary>
    internal static object? FindInTheme(string key, string themeKey)
    {
        var resources = Application.Current?.Resources;
        return resources == null ? null : Find(resources, key, themeKey, 0);
    }

    private static object? Find(ResourceDictionary dict, string key, string themeKey, int depth)
    {
        if (depth > 8) return null;
        try
        {
            if (dict.ThemeDictionaries.TryGetValue(themeKey, out var themed) && themed is ResourceDictionary td)
            {
                if (td.TryGetValue(key, out var value)) return value;
                var inner = Find(td, key, themeKey, depth + 1);
                if (inner != null) return inner;
            }
        }
        catch { }

        // Later dictionaries win, as in a normal lookup
        try
        {
            for (int i = dict.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var found = Find(dict.MergedDictionaries[i], key, themeKey, depth + 1);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }
}
