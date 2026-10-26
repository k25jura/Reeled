using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Reeled.Helpers;

/// <summary>
/// The look shared by every "pick one of these" row of buttons (Classic/Compact, Point/Range): the chosen
/// one is a standard button, the others are subtle. Never reset with Style = null: that drops WinUI's
/// implicit styles and falls back to the legacy template, which has square corners.
/// </summary>
internal static class SegmentedButtons
{
    public static void Select(Button selected, params Button[] all)
    {
        var standard = (Style)Application.Current.Resources["DefaultButtonStyle"];
        var subtle = (Style)Application.Current.Resources["SubtleButtonStyle"];
        foreach (var button in all)
        {
            button.Style = ReferenceEquals(button, selected) ? standard : subtle;
        }
    }
}
