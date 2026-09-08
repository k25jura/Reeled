using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Services;
using Reeled.ViewModels;
using Reeled.Views;

namespace Reeled;

public sealed partial class MainWindow : Window
{
    private readonly INavigationService _navigationService;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        _navigationService = App.GetService<INavigationService>();
        _navigationService.NavigatedToPlayer += OnNavigatedToPlayer;
        _navigationService.NavigatedToHome += OnNavigatedToHome;
        _navigationService.NavigatedToSettings += OnNavigatedToSettings;

        Closed += (s, e) =>
        {
            try
            {
                var playbackService = App.GetService<ILibVlcPlaybackService>();
                playbackService.Dispose();
            }
            catch { }
        };

        // Startup on HomePage
        RootFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
    }

    private void OnNavigatedToPlayer(Models.GameClip clip, System.Collections.Generic.List<Models.GameClip> playlist)
    {
        PlayerOverlayContainer.Visibility = Visibility.Visible;
        PlayerViewControl.Activate();
        var playerVM = App.GetService<PlayerViewModel>();
        playerVM.LoadClip(clip, playlist);
    }

    private void OnNavigatedToHome()
    {
        PlayerViewControl.Deactivate();
        PlayerOverlayContainer.Visibility = Visibility.Collapsed;

        if (RootFrame.Content is not HomePage)
        {
            RootFrame.Navigate(typeof(HomePage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });
        }
        else
        {
            RootFrame.Focus(FocusState.Programmatic);
        }
    }

    private void OnNavigatedToSettings()
    {
        PlayerViewControl.Deactivate();
        PlayerOverlayContainer.Visibility = Visibility.Collapsed;
        RootFrame.Navigate(typeof(SettingsPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }
}
