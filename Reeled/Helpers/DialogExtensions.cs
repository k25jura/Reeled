using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace Reeled.Helpers;

public static class DialogExtensions
{
    private static bool _open;

    /// <summary>
    /// Shows the dialog unless another one is already up (WinUI throws when two are open, which happens
    /// when a menu item is clicked twice quickly). Returns None when it couldn't be shown.
    /// </summary>
    public static async Task<ContentDialogResult> ShowSafeAsync(this ContentDialog dialog)
    {
        if (_open) return ContentDialogResult.None;
        _open = true;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (System.Exception ex)
        {
            Log.Error("Dialog", ex.Message);
            return ContentDialogResult.None;
        }
        finally
        {
            _open = false;
        }
    }
}
