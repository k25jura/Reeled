using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Reeled.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = false;
        if (value is bool flag) b = flag;
        else if (value is int count) b = count > 0;
        else if (value is long countL) b = countL > 0;
        else if (value is System.Collections.ICollection col) b = col.Count > 0;
        else if (value != null) b = true;

        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        bool isVisible = value is Visibility v && v == Visibility.Visible;
        return Invert ? !isVisible : isVisible;
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && !b;
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isNotNull = value != null;
        if (value is string s) isNotNull = !string.IsNullOrEmpty(s);
        bool shouldInvert = Invert || (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase));
        if (shouldInvert) isNotNull = !isNotNull;
        return isNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class PlayPauseGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Segoe Fluent Icons: E768 = Play, E769 = Pause
        return value is bool isPlaying && isPlaying ? "\uE769" : "\uE768";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class VolumeGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Segoe Fluent Icons: E74F = Mute, E992 = Volume 1, E993 = Volume 2, E994 = Volume 3
        if (value is bool isMuted && isMuted) return "\uE74F";
        if (parameter is int vol)
        {
            if (vol == 0) return "\uE74F";
            if (vol < 33) return "\uE992";
            if (vol < 66) return "\uE993";
            return "\uE994";
        }
        return "\uE994";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class FavoriteGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Segoe Fluent Icons: EB52 = Filled Heart, EB51 = Outline Heart
        return value is bool isFav && isFav ? "\uEB52" : "\uEB51";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class FavoriteOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool isFav && isFav ? 1.0 : 0.65;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class FavoriteForegroundConverter : IValueConverter
{
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ActiveBrush =
        new(Windows.UI.Color.FromArgb(255, 255, 229, 127)); // #FFE57F gold
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush InactiveBrush =
        new(Microsoft.UI.Colors.White); // White outline

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool isFav && isFav ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class SectionActiveBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isActive && isActive)
        {
            if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var brush))
                return brush;

            bool isLight = false;
            if (App.Window?.Content is FrameworkElement root)
            {
                isLight = (root.ActualTheme == ElementTheme.Light);
            }

            return isLight
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(20, 0, 0, 0))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255));
        }
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class SectionActiveForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isActive = value is bool b && b;
        if (isActive)
        {
            if (Application.Current.Resources.TryGetValue("AccentTextFillColorPrimaryBrush", out var brush))
                return brush;
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255));
        }

        if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var normalBrush))
            return normalBrush;

        bool isLight = false;
        if (App.Window?.Content is FrameworkElement root)
        {
            isLight = (root.ActualTheme == ElementTheme.Light);
        }

        return isLight
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(230, 20, 20, 20))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class SectionActiveFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool isActive && isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class SliderTimestampTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double progress)
        {
            try
            {
                var playerVM = App.GetService<ViewModels.PlayerViewModel>();
                if (playerVM.TotalTime > TimeSpan.Zero)
                {
                    double seconds = (Math.Clamp(progress, 0.0, 100.0) / 100.0) * playerVM.TotalTime.TotalSeconds;
                    var time = TimeSpan.FromSeconds(seconds);
                    return time.Hours > 0 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
                }
            }
            catch { }
        }
        return "00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
