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
        // E735 = Filled Star, E734 = Outline Star
        return value is bool isFav && isFav ? "\uE735" : "\uE734";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
