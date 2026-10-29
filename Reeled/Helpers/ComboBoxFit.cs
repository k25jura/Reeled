using System;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Reeled.Helpers;

/// <summary>
/// Gives a ComboBox a fixed width that fits its widest item. A ComboBox otherwise grows to that item
/// the first time its list opens, which re-wraps the text beside it and makes the whole row jump.
/// </summary>
internal static class ComboBoxFit
{
    // Beside the text: 12 px left padding, the 38 px chevron column and the 1 px borders
    private const double Chrome = 54;

    public static void Apply(ComboBox box, double minWidth)
    {
        var probe = new TextBlock { FontSize = box.FontSize, FontFamily = box.FontFamily, FontWeight = box.FontWeight };
        double widest = 0;
        foreach (var item in box.Items)
        {
            string? text = item is ContentControl control ? control.Content?.ToString() : item?.ToString();
            if (string.IsNullOrEmpty(text)) continue;
            probe.Text = text;
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }
        box.Width = Math.Max(minWidth, Math.Ceiling(widest + Chrome));
    }
}
