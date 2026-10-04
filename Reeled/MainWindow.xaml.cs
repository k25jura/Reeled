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
    private const int WM_SIZING = 0x0214;
    private const int WM_SETCURSOR = 0x0020;
    private const int IDC_ARROW = 32512;

    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;

    private readonly SUBCLASSPROC _subclassProc;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();

    public Microsoft.UI.Xaml.Controls.Frame NavigationFrame => RootFrame;

    private bool _isCursorHidden;
    private bool _isSidebarOpen;
    private bool _areCaptionControlsVisible = true;
    private bool _hasSubclassedChildWindows;
    private string? _cachedIconPath;
    private Storyboard? _transitionStoryboard;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
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
    private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

    [DllImport("User32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("User32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("User32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;


    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        EnsureWindowIcon();

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
                if (IsPlayerVisible)
                {
                    PlayerViewControl?.RefreshVideoLayout();
                }
            }
        };


        Activated += (s, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                SetCursorHidden(false);
            }
        };

        AppWindow.Closing += (s, e) =>
        {
            Helpers.CursorHelper.RestoreGlobalCursor();
        };

        Closed += (s, e) =>
        {
            Helpers.CursorHelper.RestoreGlobalCursor();
            try
            {
                var playbackService = App.GetService<ILibVlcPlaybackService>();
                playbackService.Dispose();
            }
            catch { }
        };

        RootWindowGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnWindowGlobalKeyDown), handledEventsToo: true);

        // Apply persisted app theme
        var storageService = App.GetService<ILocalStorageService>();
        App.ApplyTheme(storageService.CurrentSettings.AppTheme);

        _uiSettings.ColorValuesChanged += (sender, args) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var storage = App.GetService<ILocalStorageService>();
                if (storage.CurrentSettings.AppTheme == "Default")
                {
                    App.ApplyTheme("Default");
                }
            });
        };

        RootFrame.Navigated += (s, e) =>
        {
            if (RootFrame.Content is FrameworkElement p)
            {
                p.RequestedTheme = RootWindowGrid.RequestedTheme;
                if (p is Views.HomePage hp && hp.FindName("ClipsGridView") is FrameworkElement gv)
                {
                    gv.RequestedTheme = RootWindowGrid.RequestedTheme;
                }
                if (p is Views.SettingsPage sp && sp.FindName("SettingsScrollViewer") is FrameworkElement sv)
                {
                    sv.RequestedTheme = RootWindowGrid.RequestedTheme;
                }
            }

            if (RootFrame.Content is Views.SettingsPage)
            {
                AppTitleBar.Margin = new Thickness(96, 0, 140, 0);
            }
            else
            {
                AppTitleBar.Margin = new Thickness(48, 0, 140, 0);
            }
        };

        RootWindowGrid.ActualThemeChanged += (s, e) =>
        {
            UpdateTitleBarTheme(RootWindowGrid.ActualTheme);
        };
        UpdateTitleBarTheme(RootWindowGrid.ActualTheme);

        // Startup on HomePage
        RootFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
    }

    private void OnWindowGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (IsPlayerVisible)
        {
            PlayerViewControl.HandleKeyDown(e);
        }
    }

    public bool IsPlayerVisible => PlayerOverlayContainer?.Visibility == Visibility.Visible && PlayerOverlayContainer.IsHitTestVisible;

    private void OnNavigatedToPlayer(Models.GameClip clip, System.Collections.Generic.List<Models.GameClip> playlist)
    {
        var playerVM = App.GetService<PlayerViewModel>();
        playerVM.LoadClip(clip, playlist);

        _transitionStoryboard?.Stop();

        // Make player interactive and visible
        PlayerOverlayContainer.IsHitTestVisible = true;
        PlayerOverlayContainer.Visibility = Visibility.Visible;
        PlayerBackdropLayer.Visibility = Visibility.Visible;
        PlayerViewControl.Activate();

        // Ensure caption buttons are white while media player is active
        UpdateTitleBarTheme(ElementTheme.Dark);

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);

        var fadeInPlayer = new DoubleAnimation
        {
            From = PlayerOverlayContainer.Opacity,
            To = 1.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeInPlayer, PlayerOverlayContainer);
        Storyboard.SetTargetProperty(fadeInPlayer, "Opacity");

        var fadeInBackdrop = new DoubleAnimation
        {
            From = PlayerBackdropLayer.Opacity,
            To = 1.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeInBackdrop, PlayerBackdropLayer);
        Storyboard.SetTargetProperty(fadeInBackdrop, "Opacity");

        var fadeOutRoot = new DoubleAnimation
        {
            From = RootFrame.Opacity,
            To = 0.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeOutRoot, RootFrame);
        Storyboard.SetTargetProperty(fadeOutRoot, "Opacity");

        sb.Children.Add(fadeInPlayer);
        sb.Children.Add(fadeInBackdrop);
        sb.Children.Add(fadeOutRoot);

        sb.Completed += (s, e) =>
        {
            PlayerOverlayContainer.Opacity = 1.0;
            PlayerBackdropLayer.Opacity = 1.0;
            RootFrame.Opacity = 0.0;
        };

        _transitionStoryboard = sb;
        sb.Begin();
    }

    private void OnNavigatedToHome()
    {
        _transitionStoryboard?.Stop();

        // If player is not open (e.g. returning from Settings), return to Home instantly without delay
        if (!IsPlayerVisible)
        {
            if (RootFrame.CanGoBack)
            {
                RootFrame.GoBack();
            }
            else if (RootFrame.Content is not HomePage)
            {
                RootFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
            }
            UpdateTitleBarTheme(RootWindowGrid.ActualTheme);
            EnsureWindowIcon();
            return;
        }

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);

        var fadeOutPlayer = new DoubleAnimation
        {
            From = PlayerOverlayContainer.Opacity,
            To = 0.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeOutPlayer, PlayerOverlayContainer);
        Storyboard.SetTargetProperty(fadeOutPlayer, "Opacity");

        var fadeOutBackdrop = new DoubleAnimation
        {
            From = PlayerBackdropLayer.Opacity,
            To = 0.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeOutBackdrop, PlayerBackdropLayer);
        Storyboard.SetTargetProperty(fadeOutBackdrop, "Opacity");

        var fadeInRoot = new DoubleAnimation
        {
            From = RootFrame.Opacity,
            To = 1.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeInRoot, RootFrame);
        Storyboard.SetTargetProperty(fadeInRoot, "Opacity");

        var fadeInTitle = new DoubleAnimation
        {
            From = AppTitleBar.Opacity,
            To = 1.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeInTitle, AppTitleBar);
        Storyboard.SetTargetProperty(fadeInTitle, "Opacity");

        sb.Children.Add(fadeOutPlayer);
        sb.Children.Add(fadeOutBackdrop);
        sb.Children.Add(fadeInRoot);
        sb.Children.Add(fadeInTitle);

        sb.Completed += (s, e) =>
        {
            PlayerOverlayContainer.Opacity = 0.0;
            PlayerOverlayContainer.IsHitTestVisible = false;
            PlayerOverlayContainer.Visibility = Visibility.Collapsed;
            PlayerBackdropLayer.Visibility = Visibility.Collapsed;
            PlayerBackdropLayer.Opacity = 0.0;
            RootFrame.Opacity = 1.0;
            AppTitleBar.Opacity = 1.0;

            PlayerViewControl.Deactivate();
            SetCursorHidden(false);
            SetCaptionControlsVisible(true);
            EnsureWindowIcon();

            // Restore title bar buttons to the current app theme
            UpdateTitleBarTheme(RootWindowGrid.ActualTheme);

            if (RootFrame.Content is not HomePage)
            {
                RootFrame.Navigate(typeof(HomePage), null, new EntranceNavigationTransitionInfo());
            }
            else
            {
                RootFrame.Focus(FocusState.Programmatic);
            }
        };

        _transitionStoryboard = sb;
        sb.Begin();
    }

    private void OnNavigatedToSettings()
    {
        _transitionStoryboard?.Stop();

        PlayerViewControl.Deactivate();
        SetCursorHidden(false);
        SetCaptionControlsVisible(true);
        EnsureWindowIcon();
        PlayerOverlayContainer.Opacity = 0.0;
        PlayerOverlayContainer.IsHitTestVisible = false;
        PlayerOverlayContainer.Visibility = Visibility.Collapsed;
        PlayerBackdropLayer.Visibility = Visibility.Collapsed;
        PlayerBackdropLayer.Opacity = 0.0;
        RootFrame.Opacity = 1.0;
        AppTitleBar.Opacity = 1.0;

        // Restore title bar buttons to the current app theme
        UpdateTitleBarTheme(RootWindowGrid.ActualTheme);

        RootFrame.Navigate(typeof(SettingsPage), null, new EntranceNavigationTransitionInfo());
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

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int disableTransitions = isFullscreen ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disableTransitions, sizeof(int));
        }
        catch { }

        if (isFullscreen)
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            AppTitleBar.Visibility = Visibility.Collapsed;
            PlayerViewControl.SetFullscreenLayout(true);
        }
        else
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            _areCaptionControlsVisible = true;
            AppTitleBar.Visibility = Visibility.Visible;
            PlayerViewControl.SetFullscreenLayout(false);
            EnsureWindowIcon();
            UpdateTitleBarTheme(ElementTheme.Dark);
        }
    }

    private void EnsureChildWindowsSubclassed()
    {
        if (_hasSubclassedChildWindows) return;
        _hasSubclassedChildWindows = true;

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
    }

    public void SetCursorHidden(bool hide)
    {
        if (_isCursorHidden == hide) return;

        if (hide)
        {
            var settingsService = App.GetService<Services.ILocalStorageService>();
            if (settingsService?.CurrentSettings?.AutoHideCursor == false)
            {
                return;
            }

            _isCursorHidden = true;
            Helpers.CursorHelper.HideGlobalCursor();

            var blank = Helpers.CursorHelper.GetBlankInputCursor();
            Helpers.CursorHelper.SetElementCursor(RootWindowGrid, blank);
            Helpers.CursorHelper.SetElementCursor(PlayerOverlayContainer, blank);

            EnsureChildWindowsSubclassed();

            IntPtr blankH = Helpers.CursorHelper.GetBlankHCursor();
            if (blankH != IntPtr.Zero)
            {
                SetCursor(blankH);
            }

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                SendMessage(hwnd, WM_SETCURSOR, hwnd, (IntPtr)1);
                EnumChildWindows(hwnd, (childHwnd, lParam) =>
                {
                    SendMessage(childHwnd, WM_SETCURSOR, childHwnd, (IntPtr)1);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }
        else
        {
            _isCursorHidden = false;
            Helpers.CursorHelper.RestoreGlobalCursor();

            Helpers.CursorHelper.SetElementCursor(RootWindowGrid, null);
            Helpers.CursorHelper.SetElementCursor(PlayerOverlayContainer, null);

            SetCursor(LoadCursor(IntPtr.Zero, IDC_ARROW));

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                SendMessage(hwnd, WM_SETCURSOR, hwnd, (IntPtr)1);
                EnumChildWindows(hwnd, (childHwnd, lParam) =>
                {
                    SendMessage(childHwnd, WM_SETCURSOR, childHwnd, (IntPtr)1);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }
    }

    public void SetCaptionControlsVisible(bool visible)
    {
        if (_areCaptionControlsVisible == visible)
        {
            return;
        }

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
            _areCaptionControlsVisible = visible;
            try
            {
                presenter.SetBorderAndTitleBar(true, visible);
            }
            catch { }

            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    public void EnsureWindowIcon()
    {
        try
        {
            _cachedIconPath ??= System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(_cachedIconPath))
            {
                AppWindow.SetIcon(_cachedIconPath);
            }
        }
        catch { }
    }

    public void UpdateTitleBarTheme(ElementTheme actualTheme)
    {
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && AppWindow.TitleBar != null)
        {
            try
            {
                bool isDark = (actualTheme == ElementTheme.Dark);
                var titleBar = AppWindow.TitleBar;

                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

                if (isDark)
                {
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 255, 255, 255);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(180, 255, 255, 255);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(50, 255, 255, 255);
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(120, 255, 255, 255);
                }
                else
                {
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 24, 24, 27);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 24, 24, 27);
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 0, 0, 0);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(180, 24, 24, 27);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(50, 0, 0, 0);
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(120, 0, 0, 0);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private void ResetTitleBar()
    {
        AppTitleBar.Visibility = Visibility.Visible;
        AppTitleBar.Opacity = 1.0;
    }

    public void SetSidebarOpen(bool isOpen)
    {
        _isSidebarOpen = isOpen;
    }

    public void EnsureWidthForSidebar(double sidebarWidth)
    {
        if (AppWindow == null) return;
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized ||
                AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
            {
                return;
            }
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = (dpi > 0 ? dpi : 96) / 96.0;

        double currentWidthDip = AppWindow.Size.Width / scale;
        double targetMinDip = MinWindowWidth + sidebarWidth;

        if (currentWidthDip < targetMinDip)
        {
            int targetWidthPx = (int)Math.Round(targetMinDip * scale);

            // Check display work area to prevent expanding off-screen
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            if (displayArea != null)
            {
                int currentX = AppWindow.Position.X;
                int currentY = AppWindow.Position.Y;
                int workRight = displayArea.WorkArea.X + displayArea.WorkArea.Width;

                if (currentX + targetWidthPx > workRight)
                {
                    int newX = Math.Max(displayArea.WorkArea.X, workRight - targetWidthPx);
                    AppWindow.Move(new Windows.Graphics.PointInt32(newX, currentY));
                }
            }

            AppWindow.Resize(new Windows.Graphics.SizeInt32(targetWidthPx, AppWindow.Size.Height));
        }
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == WM_SETCURSOR && _isCursorHidden)
        {
            IntPtr blank = Helpers.CursorHelper.GetBlankHCursor();
            if (blank != IntPtr.Zero)
            {
                SetCursor(blank);
            }
            return (IntPtr)1;
        }

        if (uMsg == WM_GETMINMAXINFO)
        {
            DefSubclassProc(hWnd, uMsg, wParam, lParam);
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = (dpi > 0 ? dpi : 96) / 96.0;

            int effectiveMinWidth = _isSidebarOpen ? (MinWindowWidth + 260) : MinWindowWidth;
            mmi.ptMinTrackSize.x = (int)(effectiveMinWidth * scale);
            mmi.ptMinTrackSize.y = (int)(MinWindowHeight * scale);

            Marshal.StructureToPtr(mmi, lParam, true);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
