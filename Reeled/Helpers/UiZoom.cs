using System;

namespace Reeled.Helpers;

/// <summary>
/// Interface zoom, like a browser's or an Electron app's: Ctrl + / Ctrl - / Ctrl 0 or Settings. The
/// window scales its whole content tree (see MainWindow.ApplyZoom); this class only holds the level.
/// </summary>
public static class UiZoom
{
    public static readonly double[] Levels = { 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75 };

    public static double Current { get; private set; } = 1.0;

    /// <summary>Raised after the level changes; the argument says whether it came from a shortcut.</summary>
    public static event EventHandler<bool>? Changed;

    public static bool CanZoomIn => IndexOf(Current) < Levels.Length - 1;
    public static bool CanZoomOut => IndexOf(Current) > 0;

    public static void Load()
    {
        try
        {
            Current = Snap(App.GetService<Services.ILocalStorageService>().CurrentSettings.UiZoom);
        }
        catch
        {
            Current = 1.0;
        }
    }

    public static void Step(int direction, bool fromShortcut = false)
    {
        int index = Math.Clamp(IndexOf(Current) + Math.Sign(direction), 0, Levels.Length - 1);
        Set(Levels[index], fromShortcut);
    }

    public static void Set(double level, bool fromShortcut = false)
    {
        level = Snap(level);
        bool changed = Math.Abs(level - Current) > 0.001;
        Current = level;
        try
        {
            var storage = App.GetService<Services.ILocalStorageService>();
            if (Math.Abs(storage.CurrentSettings.UiZoom - level) > 0.001)
            {
                storage.CurrentSettings.UiZoom = level;
                _ = storage.SaveSettingsAsync(storage.CurrentSettings);
            }
        }
        catch { }
        // Shortcuts always report back (the indicator shows even at the limits)
        if (changed || fromShortcut) Changed?.Invoke(null, fromShortcut);
    }

    public static string Format(double level) => $"{Math.Round(level * 100)}%";

    private static int IndexOf(double level)
    {
        int best = 0;
        for (int i = 1; i < Levels.Length; i++)
        {
            if (Math.Abs(Levels[i] - level) < Math.Abs(Levels[best] - level)) best = i;
        }
        return best;
    }

    private static double Snap(double level) => Levels[IndexOf(double.IsFinite(level) && level > 0 ? level : 1.0)];
}
