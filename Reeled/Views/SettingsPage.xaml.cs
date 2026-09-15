using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Reeled.ViewModels;

namespace Reeled.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Initialize();

        double speed = ViewModel.DefaultPlaybackSpeed;
        foreach (ComboBoxItem item in DefaultSpeedComboBox.Items)
        {
            if (item.Tag is string tag && double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                if (Math.Abs(val - speed) < 0.01)
                {
                    DefaultSpeedComboBox.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private void OnDefaultSpeedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DefaultSpeedComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double speed))
        {
            if (Math.Abs(ViewModel.DefaultPlaybackSpeed - speed) > 0.01)
            {
                ViewModel.SetDefaultPlaybackSpeed(speed);
            }
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddFolderAsync(App.WindowHandle);
    }

    private async void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string folder)
        {
            await ViewModel.RemoveFolderAsync(folder);
        }
    }
}
