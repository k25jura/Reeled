using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Services;
using Reeled.ViewModels;
using Reeled.Views;

namespace Reeled;

public sealed partial class MainWindow : Window
{
    private readonly INavigationService _navigationService;

    // Minimum window size constraints in DIP (Density Independent Pixels)
    private const int MinWindowWidth = 760;
    private const int MinWindowHeight = 500;

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_SETCURSOR = 0x0020;
    private const int IDC_ARROW = 32512;
    private readonly SUBCLASSPROC _subclassProc;

    private bool _wasMaximizedBeforePlayer;
    private bool _isCursorHidden;
    private Storyboard? _transitionStoryboard;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("User32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("User32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("User32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("User32.dll")]
    private static extern int ShowCursor(bool bShow);

    [DllImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Subclass window to enforce minimum window dimensions at the OS level
        _subclassProc = WindowSubclassProc;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowSubclass(hwnd, _subclassProc, 1, 0);

        uint dpi = GetDpiForWindow(hwnd);
        double scale = (dpi > 0 ? dpi : 96) / 96.0;

        // Ensure window starts with a comfortable initial size (e.g. 1100x720)
        if (AppWindow.Size.Width < (int)(MinWindowWidth * scale) || AppWindow.Size.Height < (int)(MinWindowHeight * scale))
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1100 * scale), (int)(720 * scale)));
        }

        _navigationService = App.GetService<INavigationService>();
        _navigationService.NavigatedToPlayer += OnNavigatedToPlayer;
        _navigationService.NavigatedToHome += OnNavigatedToHome;
        _navigationService.NavigatedToSettings += OnNavigatedToSettings;

        AppWindow.Changed += (s, e) =>
        {
            if (e.DidSizeChange)
            {
                PlayerViewControl?.RefreshVideoLayout();
            }
        };

        Closed += (s, e) =>
        {
            try
            {
                var playbackService = App.GetService<ILibVlcPlaybackService>();
                playbackService.Dispose();
            }
            catch { }
        };

        RootWindowGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnWindowGlobalKeyDown), handledEventsToo: true);

        // Startup on HomePage
        RootFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
    }

    private void OnWindowGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (PlayerOverlayContainer.Visibility == Visibility.Visible)
        {
            PlayerViewControl.HandleKeyDown(e);
        }
    }

    private void OnNavigatedToPlayer(Models.GameClip clip, System.Collections.Generic.List<Models.GameClip> playlist)
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            _wasMaximizedBeforePlayer = (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized);
        }

        var playerVM = App.GetService<PlayerViewModel>();
        playerVM.LoadClip(clip, playlist);

        _transitionStoryboard?.Stop();

        // Phase 1: Fade out homepage and fade in solid black backdrop layer
        PlayerBackdropLayer.Visibility = Visibility.Visible;
        PlayerBackdropLayer.Opacity = 0.0;

        var phase1 = new Storyboard();
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var durationPhase1 = TimeSpan.FromMilliseconds(240);

        var fadeOutRoot = new DoubleAnimation
        {
            From = RootFrame.Opacity,
            To = 0.0,
            Duration = durationPhase1,
            EasingFunction = easeOut
        };
        Storyboard.SetTarget(fadeOutRoot, RootFrame);
        Storyboard.SetTargetProperty(fadeOutRoot, "Opacity");

        var fadeInBackdrop = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = durationPhase1,
            EasingFunction = easeOut
        };
        Storyboard.SetTarget(fadeInBackdrop, PlayerBackdropLayer);
        Storyboard.SetTargetProperty(fadeInBackdrop, "Opacity");

        var fadeOutTitle = new DoubleAnimation
        {
            From = AppTitleBar.Opacity,
            To = 0.0,
            Duration = durationPhase1,
            EasingFunction = easeOut
        };
        Storyboard.SetTarget(fadeOutTitle, AppTitleBar);
        Storyboard.SetTargetProperty(fadeOutTitle, "Opacity");

        phase1.Children.Add(fadeOutRoot);
        phase1.Children.Add(fadeInBackdrop);
        phase1.Children.Add(fadeOutTitle);

        phase1.Completed += (s, e) =>
        {
            // Maximize cleanly while screen is solid black to prevent window jumping
            if (!_wasMaximizedBeforePlayer && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
            {
                p.Maximize();
            }

            // Phase 2: Fade in video player
            PlayerOverlayContainer.Visibility = Visibility.Visible;
            PlayerOverlayContainer.Opacity = 0.0;
            PlayerViewControl.Activate();

            var phase2 = new Storyboard();
            var durationPhase2 = TimeSpan.FromMilliseconds(280);

            var fadeInPlayer = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = durationPhase2,
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(fadeInPlayer, PlayerOverlayContainer);
            Storyboard.SetTargetProperty(fadeInPlayer, "Opacity");

            phase2.Children.Add(fadeInPlayer);

            phase2.Completed += (s2, e2) =>
            {
                PlayerOverlayContainer.Opacity = 1.0;
                PlayerViewControl?.RefreshVideoLayout();
            };

            _transitionStoryboard = phase2;
            phase2.Begin();

            // Deferred layout refresh to ensure DirectX SwapChain matches final window dimensions
            DispatcherQueue.TryEnqueue(async () =>
            {
                await System.Threading.Tasks.Task.Delay(100);
                PlayerViewControl.RefreshVideoLayout();
                await System.Threading.Tasks.Task.Delay(200);
                PlayerViewControl.RefreshVideoLayout();
            });
        };

        _transitionStoryboard = phase1;
        phase1.Begin();
    }

    private void OnNavigatedToHome()
    {
        _transitionStoryboard?.Stop();

        // Phase 1: Fade out video player
        var phase1 = new Storyboard();
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        var durationPhase1 = TimeSpan.FromMilliseconds(200);

        var fadeOutPlayer = new DoubleAnimation
        {
            From = PlayerOverlayContainer.Opacity,
            To = 0.0,
            Duration = durationPhase1,
            EasingFunction = easeIn
        };
        Storyboard.SetTarget(fadeOutPlayer, PlayerOverlayContainer);
        Storyboard.SetTargetProperty(fadeOutPlayer, "Opacity");

        phase1.Children.Add(fadeOutPlayer);

        phase1.Completed += (s, e) =>
        {
            PlayerOverlayContainer.Visibility = Visibility.Collapsed;
            PlayerOverlayContainer.Opacity = 0.0;
            PlayerViewControl.Deactivate();
            SetCursorHidden(false);
            SetCaptionControlsVisible(true);

            if (!_wasMaximizedBeforePlayer && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Restore();
            }

            // Phase 2: Fade back in the homepage and titlebar, fade out black backdrop
            var phase2 = new Storyboard();
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var durationPhase2 = TimeSpan.FromMilliseconds(240);

            var fadeInRoot = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = durationPhase2,
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(fadeInRoot, RootFrame);
            Storyboard.SetTargetProperty(fadeInRoot, "Opacity");

            var fadeOutBackdrop = new DoubleAnimation
            {
                From = PlayerBackdropLayer.Opacity,
                To = 0.0,
                Duration = durationPhase2,
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(fadeOutBackdrop, PlayerBackdropLayer);
            Storyboard.SetTargetProperty(fadeOutBackdrop, "Opacity");

            var fadeInTitle = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = durationPhase2,
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(fadeInTitle, AppTitleBar);
            Storyboard.SetTargetProperty(fadeInTitle, "Opacity");

            phase2.Children.Add(fadeInRoot);
            phase2.Children.Add(fadeOutBackdrop);
            phase2.Children.Add(fadeInTitle);

            phase2.Completed += (s2, e2) =>
            {
                PlayerBackdropLayer.Visibility = Visibility.Collapsed;
                PlayerBackdropLayer.Opacity = 0.0;
                RootFrame.Opacity = 1.0;
                AppTitleBar.Opacity = 1.0;

                if (RootFrame.Content is not HomePage)
                {
                    RootFrame.Navigate(typeof(HomePage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });
                }
                else
                {
                    RootFrame.Focus(FocusState.Programmatic);
                }
            };

            _transitionStoryboard = phase2;
            phase2.Begin();
        };

        _transitionStoryboard = phase1;
        phase1.Begin();
    }

    private void OnNavigatedToSettings()
    {
        _transitionStoryboard?.Stop();

        if (!_wasMaximizedBeforePlayer && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Restore();
        }

        PlayerViewControl.Deactivate();
        SetCursorHidden(false);
        SetCaptionControlsVisible(true);
        PlayerOverlayContainer.Visibility = Visibility.Collapsed;
        PlayerOverlayContainer.Opacity = 0.0;
        PlayerBackdropLayer.Visibility = Visibility.Collapsed;
        PlayerBackdropLayer.Opacity = 0.0;
        RootFrame.Opacity = 1.0;
        AppTitleBar.Opacity = 1.0;

        RootFrame.Navigate(typeof(SettingsPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    public void ToggleFullscreen()
    {
        var playerVM = App.GetService<PlayerViewModel>();
        SetFullscreen(!playerVM.IsFullscreen);
    }

    public void SetFullscreen(bool isFullscreen)
    {
        var playerVM = App.GetService<PlayerViewModel>();
        playerVM.IsFullscreen = isFullscreen;

        if (isFullscreen)
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            AppTitleBar.Visibility = Visibility.Collapsed;
            PlayerViewControl.SetFullscreenLayout(true);
        }
        else
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
            AppTitleBar.Visibility = Visibility.Visible;
            PlayerViewControl.SetFullscreenLayout(false);
        }
    }

    public void SetCursorHidden(bool hide)
    {
        if (_isCursorHidden == hide) return;
        _isCursorHidden = hide;

        if (hide)
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                EnumChildWindows(hwnd, (childHwnd, lParam) =>
                {
                    SetWindowSubclass(childHwnd, _subclassProc, 1, 0);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }

            while (ShowCursor(false) >= 0) { }
            SetCursor(IntPtr.Zero);
        }
        else
        {
            while (ShowCursor(true) < 0) { }
            SetCursor(LoadCursor(IntPtr.Zero, IDC_ARROW));
        }
    }

    public void SetCaptionControlsVisible(bool visible)
    {
        if (AppWindow?.Presenter == null)
        {
            return;
        }

        if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
        {
            return; // In fullscreen, OS already hides caption chrome
        }

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, visible);
            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void ResetTitleBar()
    {
        AppTitleBar.Visibility = Visibility.Visible;
        AppTitleBar.Opacity = 1.0;
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == WM_SETCURSOR && _isCursorHidden)
        {
            SetCursor(IntPtr.Zero);
            return (IntPtr)1;
        }

        var result = DefSubclassProc(hWnd, uMsg, wParam, lParam);
        if (uMsg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = (dpi > 0 ? dpi : 96) / 96.0;

            mmi.ptMinTrackSize.x = (int)(MinWindowWidth * scale);
            mmi.ptMinTrackSize.y = (int)(MinWindowHeight * scale);

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        return result;
    }
}
