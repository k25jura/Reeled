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
