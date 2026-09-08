using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Reeled.Models;
using Reeled.ViewModels;

namespace Reeled.Views;

public sealed partial class PlayerPage : Page
{
    public PlayerViewModel ViewModel { get; }
    private readonly DispatcherTimer _inactivityTimer;
    private bool _isUserDraggingSlider;

    public PlayerPage()
    {
        InitializeComponent();
        ViewModel = App.GetService<PlayerViewModel>();

        _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _inactivityTimer.Tick += (s, e) =>
        {
            if (ViewModel.IsPlaying && !ViewModel.IsSidebarOpen)
            {
                ViewModel.IsControlsVisible = false;
            }
        };

        // Wire up pointer handlers on TimelineSlider with handledEventsToo = true to detect manual scrubbing
        TimelineSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnTimelineSliderPointerPressed), true);
        TimelineSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);
        TimelineSlider.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);
        TimelineSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnTimelineSliderPointerReleased), true);

        Loaded += (s, e) =>
        {
            this.Focus(FocusState.Programmatic);
            _inactivityTimer.Start();
        };

        Unloaded += (s, e) =>
        {
            _inactivityTimer.Stop();
            PlayerVideoView.MediaPlayer = null;
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        this.Focus(FocusState.Programmatic);
        ViewModel.IsControlsVisible = true;
    }

    private void OnVideoViewInitialized(object? sender, LibVLCSharp.Platforms.Windows.InitializedEventArgs e)
    {
        ViewModel.AttachVideoView(PlayerVideoView, e.SwapChainOptions);
    }

    private void OnPagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsControlsVisible = true;
        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }

    private void OnTimelineSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isUserDraggingSlider = true;
        ViewModel.OnSliderDragStarted();
    }

    private void OnTimelineSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isUserDraggingSlider)
        {
            _isUserDraggingSlider = false;
            ViewModel.OnSliderDragCompleted(TimelineSlider.Value);
        }
    }

    private void OnTimelineSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUserDraggingSlider)
        {
            ViewModel.OnSliderDeltaChanged(e.NewValue);
        }
    }

    private void OnVolumeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ViewModel.SetVolume((int)e.NewValue);
    }

    private void OnSpeedItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tag && double.TryParse(tag, out double speed))
        {
            ViewModel.ChangeSpeed(speed);
        }
    }

    private void OnFullscreenButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        var appWindow = App.Window.AppWindow;
        if (appWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
        {
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            ViewModel.IsFullscreen = false;
        }
        else
        {
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            ViewModel.IsFullscreen = true;
        }
    }

    private async void OnAddMarkerClick(object sender, RoutedEventArgs e)
    {
        var textBox = new TextBox
        {
            PlaceholderText = "E.g., Ace, Clutch, Headshot (or leave blank)",
            Text = $"Mark at {ViewModel.FormattedCurrentTime}"
        };

        var dialog = new ContentDialog
        {
            Title = "Save Moment Bookmark",
            Content = textBox,
            PrimaryButtonText = "Save Mark",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.AddBookmarkAsync(textBox.Text);
        }
    }

    private void OnBookmarksButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            var flyout = new MenuFlyout();

            if (ViewModel.Bookmarks.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem { Text = "No moments marked yet. Press 'B' to mark.", IsEnabled = false });
            }
            else
            {
                foreach (var bm in ViewModel.Bookmarks)
                {
                    var item = new MenuFlyoutItem
                    {
                        Text = $"{bm.FormattedTimestamp} - {bm.Label}",
                        Icon = new FontIcon { Glyph = "\uE735" }
                    };
                    item.Click += (s, args) => ViewModel.JumpToBookmark(bm);
                    flyout.Items.Add(item);
                }
            }

            flyout.Items.Add(new MenuFlyoutSeparator());
            var addMarkItem = new MenuFlyoutItem { Text = "Add New Mark (B)", Icon = new FontIcon { Glyph = "\uE710" } };
            addMarkItem.Click += (s, args) => OnAddMarkerClick(sender, e);
            flyout.Items.Add(addMarkItem);

            flyout.ShowAt(element);
        }
    }

    private void OnPlaylistSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is GameClip clip)
        {
            ViewModel.SelectPlaylistClip(clip);
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShift = shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case VirtualKey.Space:
                ViewModel.TogglePlayPause();
                e.Handled = true;
                break;

            case VirtualKey.Left:
                if (isShift) ViewModel.FineBackward();
                else ViewModel.SkipBackward();
                e.Handled = true;
                break;

            case VirtualKey.Right:
                if (isShift) ViewModel.FineForward();
                else ViewModel.SkipForward();
                e.Handled = true;
                break;

            case VirtualKey.Up:
                ViewModel.SetVolume(ViewModel.Volume + 5);
                e.Handled = true;
                break;

            case VirtualKey.Down:
                ViewModel.SetVolume(ViewModel.Volume - 5);
                e.Handled = true;
                break;

            case VirtualKey.M:
                ViewModel.ToggleMute();
                e.Handled = true;
                break;

            case VirtualKey.B:
                _ = ViewModel.AddBookmarkAsync();
                e.Handled = true;
                break;

            case VirtualKey.F:
            case VirtualKey.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (App.Window.AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                {
                    ToggleFullscreen();
                }
                else
                {
                    ViewModel.BackToHome();
                }
                e.Handled = true;
                break;
        }
    }
}
