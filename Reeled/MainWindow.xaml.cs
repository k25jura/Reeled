using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
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
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_ACTIVATEAPP = 0x001C;
    private const int WM_KILLFOCUS = 0x0008;
    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_SIZE = 0x0005;
    private const int SIZE_MINIMIZED = 1;
    private const int WM_DESTROY = 0x0002;
    private const int WA_INACTIVE = 0;
    private const int IDC_ARROW = 32512;
    private const int HTCLIENT = 1;

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
    /// <summary>The (zoomable) grid holding all app content.</summary>
    public FrameworkElement ContentRoot => RootWindowGrid;

    private bool _isCursorHidden;
    private bool _isWindowActive = true;
    private bool _isPlayerClosing;
    public bool IsWindowActive
    {
        get
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (IsIconic(hwnd))
                {
                    _isWindowActive = false;
                    return false;
                }

                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero)
                {
                    return _isWindowActive;
                }

                if (fg == hwnd || IsChild(hwnd, fg) || GetAncestor(fg, GA_ROOTOWNER) == hwnd)
                {
                    _isWindowActive = true;
                    return true;
                }

                GetWindowThreadProcessId(fg, out uint pid);
                if (pid != 0 && pid == Environment.ProcessId)
                {
                    _isWindowActive = true;
                    return true;
                }

                _isWindowActive = false;
                return false;
            }
            catch
            {
                return _isWindowActive;
            }
        }
    }
    private bool _isSidebarOpen;
    private bool _areCaptionControlsVisible = true;
    private bool _desiredCaptionControlsVisible = true;
    private readonly SUBCLASSPROC _childSubclassProc;
    private string? _cachedIconPath;
    private Storyboard? _transitionStoryboard;
    private int _dragDepth;
    private Storyboard? _dropOverlayStoryboard;
    private bool _isDropOverlayVisible;

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

    [DllImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("User32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("User32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    private const uint GA_ROOTOWNER = 3;

    [DllImport("User32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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
        _childSubclassProc = ChildWindowSubclassProc;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowSubclass(hwnd, _subclassProc, 1, 0);

        uint dpi = GetDpiForWindow(hwnd);
        double scale = (dpi > 0 ? dpi : 96) / 96.0;

        // Reopen where the window was last time; first launch gets a comfortable 1100x720
        if (!RestoreWindowBounds(scale) &&
            (AppWindow.Size.Width < (int)(MinWindowWidth * scale) || AppWindow.Size.Height < (int)(MinWindowHeight * scale)))
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1100 * scale), (int)(720 * scale)));
        }
        AppWindow.Changed += (s, e) =>
        {
            if (e.DidSizeChange || e.DidPositionChange || e.DidPresenterChange) RememberWindowBounds();
        };

        _navigationService = App.GetService<INavigationService>();
        _navigationService.NavigatedToPlayer += OnNavigatedToPlayer;
        _navigationService.NavigatedToHome += OnNavigatedToHome;
        _navigationService.NavigatedToSettings += OnNavigatedToSettings;

        AppWindow.Changed += (s, e) =>
        {
            if (e.DidPresenterChange)
            {
                if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                {
                    if (AppTitleBar != null)
                    {
                        AppTitleBar.Visibility = Visibility.Collapsed;
                    }
                    SetTitleBar(null);
                }
                else if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter)
                {
                    bool shouldShowCaption = true;
                    if (IsPlayerVisible)
                    {
                        var playerVM = App.GetService<PlayerViewModel>();
                        shouldShowCaption = playerVM?.IsControlsVisible ?? true;
                    }
                    SetCaptionControlsVisible(shouldShowCaption, force: true);
                    UpdateTitleBarTheme(IsPlayerVisible ? ElementTheme.Dark : RootWindowGrid.ActualTheme);
                }
            }

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
            _isWindowActive = (e.WindowActivationState != WindowActivationState.Deactivated);
            if (!_isWindowActive)
            {
                SetCursorHidden(false);
            }
        };

        AppWindow.Closing += (s, e) =>
        {
            // Optional VLC-habit guard: closing while a clip plays goes back to the library instead
            if (IsPlayerVisible &&
                App.GetService<ILocalStorageService>().CurrentSettings.CloseButtonReturnsToLibrary)
            {
                e.Cancel = true;
                if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                {
                    ToggleFullscreen();
                }
                App.GetService<PlayerViewModel>().BackToHome();
                return;
            }

            Helpers.CursorHelper.RestoreGlobalCursor();
            FlushStateOnExit();
            try
            {
                var playbackService = App.GetService<ILibVlcPlaybackService>();
                playbackService.Dispose();
            }
            catch { }
            try
            {
                SingleInstanceService.Stop();
            }
            catch { }
        };

        Closed += (s, e) =>
        {
            Helpers.CursorHelper.RestoreGlobalCursor();
            FlushStateOnExit();
            try
            {
                var playbackService = App.GetService<ILibVlcPlaybackService>();
                playbackService.Dispose();
            }
            catch { }

            try
            {
                SingleInstanceService.Stop();
            }
            catch { }

            try
            {
                Microsoft.UI.Xaml.Application.Current?.Exit();
            }
            catch { }

            Environment.Exit(0);
        };

        // On the window root so keys arrive even when nothing inside has focus
        ZoomHost.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnWindowGlobalKeyDown), handledEventsToo: true);
        InitializeZoom();

        // Apply persisted app theme
        var storageService = App.GetService<ILocalStorageService>();
        App.ApplyTheme(storageService.CurrentSettings.AppTheme);

        _uiSettings.ColorValuesChanged += (sender, args) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Helpers.Accent.OnSystemColorsChanged();
                var storage = App.GetService<ILocalStorageService>();
                if (storage.CurrentSettings.AppTheme == "Default")
                {
                    App.ApplyTheme("Default");
                }
            });

            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(150);
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
                if (p is Views.HomePage hp)
                {
                    hp.ApplyThemeVisuals(RootWindowGrid.RequestedTheme);
                }
                if (p is Views.SettingsPage sp)
                {
                    sp.ApplyThemeVisuals(RootWindowGrid.RequestedTheme);
                }
            }

            if (RootFrame.Content is Views.SettingsPage)
            {
                AppTitleBar.Margin = new Thickness(540, 0, CaptionInset, 0);
            }
            else
            {
                AppTitleBar.Margin = new Thickness(48, 0, CaptionInset, 0);
            }
        };

        RootWindowGrid.ActualThemeChanged += (s, e) =>
        {
            UpdateTitleBarTheme(RootWindowGrid.ActualTheme);
            if (RootFrame.Content is Views.HomePage hp)
            {
                hp.ApplyThemeVisuals(RootWindowGrid.ActualTheme);
            }
            else if (RootFrame.Content is Views.SettingsPage sp)
            {
                sp.ApplyThemeVisuals(RootWindowGrid.ActualTheme);
            }
        };
        UpdateTitleBarTheme(RootWindowGrid.ActualTheme);

        UpdateDropOverlayLocalization();
        var locService = App.GetService<ILocalizationService>();
        if (locService != null)
        {
            locService.LanguageChanged += (s, e) => DispatcherQueue.TryEnqueue(UpdateDropOverlayLocalization);
        }

        // Startup on HomePage
        RootFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
    }

    private bool _stateFlushed;

    /// <summary>Restores the saved bounds when they still land on a connected display.</summary>
    private bool RestoreWindowBounds(double scale)
    {
        try
        {
            var settings = App.GetService<ILocalStorageService>().CurrentSettings;
            if (settings.WindowWidth < MinWindowWidth * scale || settings.WindowHeight < MinWindowHeight * scale) return false;

            var rect = new Windows.Graphics.RectInt32(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight);
            // A monitor that was unplugged must not leave the window off-screen
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromRect(rect, Microsoft.UI.Windowing.DisplayAreaFallback.None);
            if (area == null) return false;
            var work = area.WorkArea;
            int width = Math.Min(rect.Width, work.Width);
            int height = Math.Min(rect.Height, work.Height);
            int x = Math.Clamp(rect.X, work.X - width / 2, work.X + work.Width - width / 2);
            int y = Math.Clamp(rect.Y, work.Y, work.Y + work.Height - 48);
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));

            if (settings.WindowMaximized && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
            return true;
        }
        catch (Exception ex)
        {
            Helpers.Log.Error("RestoreWindowBounds", ex);
            return false;
        }
    }

    /// <summary>
    /// Keeps the last normal bounds up to date (maximize and the player's full screen don't overwrite
    /// them), so the next launch reopens at the same size and place.
    /// </summary>
    private void RememberWindowBounds()
    {
        try
        {
            if (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter) return;
            var settings = App.GetService<ILocalStorageService>().CurrentSettings;
            switch (presenter.State)
            {
                case Microsoft.UI.Windowing.OverlappedPresenterState.Maximized:
                    settings.WindowMaximized = true;
                    break;
                case Microsoft.UI.Windowing.OverlappedPresenterState.Restored:
                    settings.WindowMaximized = false;
                    settings.WindowX = AppWindow.Position.X;
                    settings.WindowY = AppWindow.Position.Y;
                    settings.WindowWidth = AppWindow.Size.Width;
                    settings.WindowHeight = AppWindow.Size.Height;
                    break;
            }
        }
        catch { }
    }

    /// <summary>
    /// Settings and the metadata cache are saved in the background during normal use; persist them
    /// synchronously before the process exits so the last changes aren't lost.
    /// </summary>
    private void FlushStateOnExit()
    {
        if (_stateFlushed) return;
        _stateFlushed = true;

        try
        {
            if (IsPlayerVisible)
            {
                App.GetService<PlayerViewModel>().SaveResumePosition();
            }
        }
        catch { }

        try { App.GetService<IClipMetadataCacheService>().Flush(); } catch { }

        try
        {
            var storage = App.GetService<ILocalStorageService>();
            storage.SaveSettingsSync(storage.CurrentSettings);
        }
        catch { }

        // A downloaded update installs once the app has closed
        try { App.GetService<ViewModels.SettingsViewModel>().ApplyPendingUpdateOnExit(); } catch { }
    }

    private void OnWindowGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TryHandleZoomKey(e)) return;
        if (e.Handled) return;
        if (IsPlayerVisible)
        {
            PlayerViewControl.HandleKeyDown(e);
        }
    }

    public bool IsPlayerVisible => !_isPlayerClosing && PlayerOverlayContainer?.Visibility == Visibility.Visible && PlayerOverlayContainer.IsHitTestVisible;

    private void OnNavigatedToPlayer(Models.GameClip clip, System.Collections.Generic.List<Models.GameClip> playlist)
    {
        var playerVM = App.GetService<PlayerViewModel>();
        if (IsPlayerVisible && playerVM.CurrentClip?.FilePath == clip.FilePath)
        {
            return;
        }

        _isPlayerClosing = false;
        playerVM.LoadClip(clip, playlist);

        _transitionStoryboard?.Stop();

        // Make player interactive and visible
        PlayerOverlayContainer.IsHitTestVisible = true;
        PlayerOverlayContainer.Visibility = Visibility.Visible;
        PlayerBackdropLayer.Visibility = Visibility.Visible;
        PlayerViewControl.Activate();

        // Ensure caption buttons are white while media player is active
        SetCaptionControlsVisible(true, force: true);
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
        Helpers.Motion.Begin(sb);
    }

    private void OnNavigatedToHome()
    {
        _isPlayerClosing = true;
        _transitionStoryboard?.Stop();

        // If player is not open (e.g. returning from Settings), return to Home instantly without delay
        if (PlayerOverlayContainer?.Visibility != Visibility.Visible)
        {
            if (RootFrame.CanGoBack)
            {
                RootFrame.GoBack();
            }
            else if (RootFrame.Content is not HomePage)
            {
                RootFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
            }
            _isPlayerClosing = false;
            SetCaptionControlsVisible(true, force: true);
            UpdateTitleBarTheme(RootWindowGrid.ActualTheme);
            EnsureWindowIcon();
            return;
        }

        // Deactivate and collapse DirectX video surface immediately to prevent ghost boxes during transition
        var playerVM = App.GetService<PlayerViewModel>();
        if (playerVM?.IsFullscreen == true)
        {
            SetFullscreen(false);
        }

        PlayerOverlayContainer.IsHitTestVisible = false;
        PlayerViewControl.Deactivate();
        SetCursorHidden(false);
        SetCaptionControlsVisible(true, force: true);
        UpdateTitleBarTheme(RootWindowGrid.ActualTheme);
        EnsureWindowIcon();

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
            _isPlayerClosing = false;
            PlayerOverlayContainer.Opacity = 0.0;
            PlayerOverlayContainer.IsHitTestVisible = false;
            PlayerOverlayContainer.Visibility = Visibility.Collapsed;
            PlayerBackdropLayer.Visibility = Visibility.Collapsed;
            PlayerBackdropLayer.Opacity = 0.0;
            RootFrame.Opacity = 1.0;
            AppTitleBar.Opacity = 1.0;

            EnsureWindowIcon();
            App.GetService<ViewModels.HomeViewModel>()?.UpdateBookmarkCounts();

            // Restore title bar buttons to the current app theme
            SetCaptionControlsVisible(true, force: true);
            UpdateTitleBarTheme(RootWindowGrid.ActualTheme);

            if (RootFrame.Content is not HomePage)
            {
                RootFrame.Navigate(typeof(HomePage), null, new EntranceNavigationTransitionInfo());
            }
            else if (RootFrame.Content is HomePage homePage)
            {
                homePage.OnReturnedFromViewer();
            }
        };

        _transitionStoryboard = sb;
        Helpers.Motion.Begin(sb);
    }

    private void OnNavigatedToSettings()
    {
        _isPlayerClosing = true;
        _transitionStoryboard?.Stop();

        var playerVM = App.GetService<PlayerViewModel>();
        if (playerVM?.IsFullscreen == true)
        {
            SetFullscreen(false);
        }

        PlayerOverlayContainer.Opacity = 0.0;
        PlayerOverlayContainer.IsHitTestVisible = false;
        PlayerOverlayContainer.Visibility = Visibility.Collapsed;
        PlayerBackdropLayer.Visibility = Visibility.Collapsed;
        PlayerBackdropLayer.Opacity = 0.0;
        PlayerViewControl.Deactivate();
        SetCursorHidden(false);
        _isPlayerClosing = false;
        SetCaptionControlsVisible(true, force: true);
        EnsureWindowIcon();
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
        if (playerVM != null)
        {
            playerVM.IsFullscreen = isFullscreen;
        }

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
            SetTitleBar(null);
            PlayerViewControl.SetFullscreenLayout(true);
        }
        else
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            PlayerViewControl.SetFullscreenLayout(false);

            bool shouldShowCaption = true;
            if (IsPlayerVisible && playerVM != null)
            {
                shouldShowCaption = playerVM.IsControlsVisible;
            }

            SetCaptionControlsVisible(shouldShowCaption, force: true);
            EnsureWindowIcon();
            UpdateTitleBarTheme(IsPlayerVisible ? ElementTheme.Dark : RootWindowGrid.ActualTheme);
        }

        if (IsPlayerVisible)
        {
            PlayerViewControl.SyncControlsVisibility(forceCursor: true);
        }
    }

    private void EnsureChildWindowsSubclassed()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            EnumChildWindows(hwnd, (childHwnd, lParam) =>
            {
                SetWindowSubclass(childHwnd, _childSubclassProc, 2, 0);
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    public void SetCursorHidden(bool hide, bool force = false)
    {
        if (!hide)
        {
            _isCursorHidden = false;
            Helpers.CursorHelper.RestoreGlobalCursor();

            Helpers.CursorHelper.SetElementCursor(RootWindowGrid, null);
            Helpers.CursorHelper.SetElementCursor(PlayerOverlayContainer, null);
            PlayerViewControl?.SetCursorHidden(false);

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
            return;
        }

        if (_isCursorHidden && !force) return;

        var settingsService = App.GetService<Services.ILocalStorageService>();
        if (settingsService?.CurrentSettings?.AutoHideCursor == false)
        {
            return;
        }

        if (!IsWindowActive)
        {
            return;
        }

        _isCursorHidden = true;
        Helpers.CursorHelper.HideGlobalCursor(force);

        var blank = Helpers.CursorHelper.GetBlankInputCursor();
        Helpers.CursorHelper.SetElementCursor(PlayerOverlayContainer, blank);
        PlayerViewControl?.SetCursorHidden(true);

        EnsureChildWindowsSubclassed();

        IntPtr blankH = Helpers.CursorHelper.GetBlankHCursor();
        if (blankH != IntPtr.Zero && IsPlayerVisible && IsWindowActive)
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

    public void SetCaptionControlsVisible(bool visible, bool force = false)
    {
        if (!visible && !IsPlayerVisible)
        {
            // Outside of active player overlay, caption controls MUST NEVER be hidden
            visible = true;
        }

        _desiredCaptionControlsVisible = visible;

        if (AppWindow?.Presenter == null)
        {
            return;
        }

        if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
        {
            if (AppTitleBar != null)
            {
                AppTitleBar.Visibility = Visibility.Collapsed;
            }
            SetTitleBar(null);
            return; // In fullscreen, OS already hides caption chrome; record desired state
        }

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            bool needUpdate = force ||
                              _areCaptionControlsVisible != visible ||
                              presenter.HasTitleBar != visible ||
                              (visible && (AppTitleBar?.Visibility != Visibility.Visible || AppTitleBar?.Opacity < 1.0));

            if (needUpdate)
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
                    if (visible)
                    {
                        AppTitleBar.Opacity = 1.0;
                    }
                    SetTitleBar(visible ? AppTitleBar : null);
                }

                if (visible)
                {
                    ExtendsContentIntoTitleBar = true;
                    UpdateTitleBarTheme(IsPlayerVisible ? ElementTheme.Dark : RootWindowGrid.ActualTheme);
                    EnsureWindowIcon();
                }
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

    // ---------------------------------------------------------------------------------------------
    // Interface zoom
    // ---------------------------------------------------------------------------------------------

    /// <summary>Space the caption buttons take, in (zoomed) content units.</summary>
    private static double CaptionInset => 140 / Helpers.UiZoom.Current;

    private DispatcherTimer? _zoomIndicatorTimer;
    private bool _isZoomIndicatorShown;
    private bool _isPointerOverZoomIndicator;

    private void InitializeZoom()
    {
        Helpers.UiZoom.Load();
        Helpers.UiZoom.Changed += (s, fromShortcut) =>
        {
            ApplyZoom();
            if (fromShortcut) ShowZoomIndicator();
            else UpdateZoomIndicatorText();
        };
        ApplyZoom();
    }

    /// <summary>
    /// Scales the whole content tree: the content is laid out at window size / zoom and rendered with a
    /// matching scale transform, so text and vectors stay sharp and hit-testing follows the transform.
    /// </summary>
    private void ApplyZoom()
    {
        double zoom = Helpers.UiZoom.Current;
        if (Math.Abs(zoom - 1) < 0.001)
        {
            RootWindowGrid.RenderTransform = null;
            RootWindowGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            RootWindowGrid.VerticalAlignment = VerticalAlignment.Stretch;
            RootWindowGrid.Width = double.NaN;
            RootWindowGrid.Height = double.NaN;
            RootWindowGrid.MinWidth = MinWindowWidth;
            RootWindowGrid.MinHeight = MinWindowHeight;
        }
        else
        {
            RootWindowGrid.HorizontalAlignment = HorizontalAlignment.Left;
            RootWindowGrid.VerticalAlignment = VerticalAlignment.Top;
            RootWindowGrid.MinWidth = 0;
            RootWindowGrid.MinHeight = 0;
            RootWindowGrid.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = zoom, ScaleY = zoom };
            SizeZoomedContent();
            EnsureWindowFitsZoom(zoom);
        }

        // Title bar drag region and caption-button clearance are in window units
        var m = AppTitleBar.Margin;
        AppTitleBar.Margin = new Thickness(m.Left, 0, CaptionInset, 0);
        ZoomLevelChanged?.Invoke(this, zoom);
        if (IsPlayerVisible) PlayerViewControl?.RefreshVideoLayout();
    }

    /// <summary>Pages that keep clear of the caption buttons adjust their insets here.</summary>
    public event EventHandler<double>? ZoomLevelChanged;

    private void SizeZoomedContent()
    {
        double zoom = Helpers.UiZoom.Current;
        if (Math.Abs(zoom - 1) < 0.001 || ZoomHost.ActualWidth <= 0) return;
        RootWindowGrid.Width = ZoomHost.ActualWidth / zoom;
        RootWindowGrid.Height = ZoomHost.ActualHeight / zoom;
    }

    private void OnZoomHostSizeChanged(object sender, SizeChangedEventArgs e) => SizeZoomedContent();

    /// <summary>Grows a restored window so zoomed-in content still gets the minimum layout size.</summary>
    private void EnsureWindowFitsZoom(double zoom)
    {
        if (zoom <= 1 || AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter ||
            presenter.State != Microsoft.UI.Windowing.OverlappedPresenterState.Restored) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = (dpi > 0 ? dpi : 96) / 96.0;
        int minW = (int)(MinWindowWidth * scale * zoom);
        int minH = (int)(MinWindowHeight * scale * zoom);
        if (AppWindow.Size.Width >= minW && AppWindow.Size.Height >= minH) return;

        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        int w = Math.Max(AppWindow.Size.Width, minW);
        int h = Math.Max(AppWindow.Size.Height, minH);
        if (area != null)
        {
            w = Math.Min(w, area.WorkArea.Width);
            h = Math.Min(h, area.WorkArea.Height);
            int x = Math.Clamp(AppWindow.Position.X, area.WorkArea.X, area.WorkArea.X + area.WorkArea.Width - w);
            int y = Math.Clamp(AppWindow.Position.Y, area.WorkArea.Y, area.WorkArea.Y + area.WorkArea.Height - h);
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        }
        else
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        }
    }

    /// <summary>Ctrl + / Ctrl - / Ctrl 0 (main row and numpad), anywhere in the app.</summary>
    private bool TryHandleZoomKey(KeyRoutedEventArgs e)
    {
        var down = Windows.UI.Core.CoreVirtualKeyStates.Down;
        if (!Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(down)) return false;
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(down)) return false;
        bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(down);

        int key = (int)e.Key;
        const int VK_OEM_PLUS = 0xBB, VK_OEM_MINUS = 0xBD;
        if (key == VK_OEM_PLUS || e.Key == Windows.System.VirtualKey.Add)
        {
            Helpers.UiZoom.Step(+1, fromShortcut: true);
        }
        else if (key == VK_OEM_MINUS || e.Key == Windows.System.VirtualKey.Subtract)
        {
            Helpers.UiZoom.Step(-1, fromShortcut: true);
        }
        else if ((e.Key == Windows.System.VirtualKey.Number0 || e.Key == Windows.System.VirtualKey.NumberPad0) && !shift)
        {
            Helpers.UiZoom.Set(1.0, fromShortcut: true);
        }
        else
        {
            return false;
        }
        e.Handled = true;
        return true;
    }

    private void UpdateZoomIndicatorText()
    {
        if (ZoomIndicatorText == null) return;
        ZoomIndicatorText.Text = Helpers.UiZoom.Format(Helpers.UiZoom.Current);
        ZoomInIndicatorButton.IsEnabled = Helpers.UiZoom.CanZoomIn;
        ZoomOutIndicatorButton.IsEnabled = Helpers.UiZoom.CanZoomOut;
        ZoomResetIndicatorButton.Content = App.GetService<ILocalizationService>()["Zoom_Reset"];
    }

    private void ShowZoomIndicator()
    {
        UpdateZoomIndicatorText();
        ZoomIndicator.RequestedTheme = IsPlayerVisible ? ElementTheme.Dark : App.CurrentTheme;
        if (!_isZoomIndicatorShown)
        {
            _isZoomIndicatorShown = true;
            ZoomIndicator.Visibility = Visibility.Visible;
            var sb = new Storyboard();
            sb.Children.Add(Helpers.Motion.Spline(ZoomIndicator, "Opacity", 1, Helpers.Motion.Medium, from: ZoomIndicator.Opacity));
            sb.Children.Add(Helpers.Motion.Spline(ZoomIndicatorTranslation, "Y", 0, Helpers.Motion.Medium, from: ZoomIndicatorTranslation.Y));
            Helpers.Motion.BeginExact(sb);
        }
        RestartZoomIndicatorTimer();
    }

    private void RestartZoomIndicatorTimer()
    {
        _zoomIndicatorTimer ??= CreateZoomIndicatorTimer();
        _zoomIndicatorTimer.Stop();
        _zoomIndicatorTimer.Start();
    }

    private DispatcherTimer CreateZoomIndicatorTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            if (_isPointerOverZoomIndicator) return;
            var sb = new Storyboard();
            sb.Children.Add(Helpers.Motion.Spline(ZoomIndicator, "Opacity", 0, Helpers.Motion.Short, accelerate: true));
            sb.Children.Add(Helpers.Motion.Spline(ZoomIndicatorTranslation, "Y", -8, Helpers.Motion.Short, accelerate: true));
            sb.Completed += (s2, e2) =>
            {
                if (timer.IsEnabled || _isPointerOverZoomIndicator) return;
                _isZoomIndicatorShown = false;
                ZoomIndicator.Visibility = Visibility.Collapsed;
            };
            Helpers.Motion.BeginExact(sb);
        };
        return timer;
    }

    private void OnZoomIndicatorPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverZoomIndicator = true;
        _zoomIndicatorTimer?.Stop();
        if (ZoomIndicator.Opacity < 1) ShowZoomIndicator();
    }

    private void OnZoomIndicatorPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverZoomIndicator = false;
        RestartZoomIndicatorTimer();
    }

    private void OnZoomInIndicatorClick(object sender, RoutedEventArgs e) => Helpers.UiZoom.Step(+1, fromShortcut: true);
    private void OnZoomOutIndicatorClick(object sender, RoutedEventArgs e) => Helpers.UiZoom.Step(-1, fromShortcut: true);
    private void OnZoomResetIndicatorClick(object sender, RoutedEventArgs e) => Helpers.UiZoom.Set(1.0, fromShortcut: true);


    public void UpdateSettingsTitleBarMargin(double leftOffset)
    {
        if (RootFrame.Content is Views.SettingsPage)
        {
            AppTitleBar.Margin = new Thickness(Math.Max(leftOffset, 540), 0, CaptionInset, 0);
        }
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

    private IntPtr ChildWindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == WM_SETCURSOR && _isCursorHidden)
        {
            if (IsPlayerVisible && IsWindowActive && (lParam.ToInt64() & 0xFFFF) == HTCLIENT)
            {
                IntPtr blank = Helpers.CursorHelper.GetBlankHCursor();
                if (blank != IntPtr.Zero)
                {
                    SetCursor(blank);
                    return (IntPtr)1;
                }
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
    {
        var mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hWnd == mainHwnd)
        {
            if (uMsg == WM_ACTIVATE)
            {
                bool inactive = (wParam.ToInt64() & 0xFFFF) == WA_INACTIVE;
                _isWindowActive = !inactive;
                if (inactive)
                {
                    SetCursorHidden(false);
                }
            }
            else if (uMsg == WM_ACTIVATEAPP)
            {
                bool active = (wParam != IntPtr.Zero);
                _isWindowActive = active;
                if (!active)
                {
                    SetCursorHidden(false);
                }
            }
            else if (uMsg == WM_SIZE && wParam.ToInt64() == SIZE_MINIMIZED)
            {
                _isWindowActive = false;
                SetCursorHidden(false);
            }
            else if (uMsg == WM_DESTROY)
            {
                _isWindowActive = false;
                SetCursorHidden(false);
            }
        }

        if (uMsg == WM_SETCURSOR && _isCursorHidden)
        {
            if (IsPlayerVisible && IsWindowActive && (lParam.ToInt64() & 0xFFFF) == HTCLIENT)
            {
                IntPtr blank = Helpers.CursorHelper.GetBlankHCursor();
                if (blank != IntPtr.Zero)
                {
                    SetCursor(blank);
                    return (IntPtr)1;
                }
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        if (uMsg == WM_GETMINMAXINFO)
        {
            DefSubclassProc(hWnd, uMsg, wParam, lParam);
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = (dpi > 0 ? dpi : 96) / 96.0;

            // Zoomed-in content needs proportionally more room
            double zoom = Helpers.UiZoom.Current;
            int effectiveMinWidth = _isSidebarOpen ? (MinWindowWidth + 260) : MinWindowWidth;
            mmi.ptMinTrackSize.x = (int)(effectiveMinWidth * scale * Math.Max(1, zoom));
            mmi.ptMinTrackSize.y = (int)(MinWindowHeight * scale * Math.Max(1, zoom));

            Marshal.StructureToPtr(mmi, lParam, true);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void UpdateDropOverlayLocalization()
    {
        var loc = App.GetService<ILocalizationService>();
        if (loc == null) return;
        if (DropOverlayTitleText != null) DropOverlayTitleText.Text = loc["DropOverlay_Title"];
        if (DropOverlaySubtitleText != null) DropOverlaySubtitleText.Text = loc["DropOverlay_Subtitle"];
    }

    private void OnRootWindowDragEnter(object sender, DragEventArgs e)
    {
        _dragDepth++;
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            ShowDropOverlay(true);
        }
    }

    private void OnRootWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            if (!_isDropOverlayVisible)
            {
                ShowDropOverlay(true);
            }
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void OnRootWindowDragLeave(object sender, DragEventArgs e)
    {
        _dragDepth--;
        if (_dragDepth <= 0)
        {
            _dragDepth = 0;
            ShowDropOverlay(false);
        }
    }

    private async void OnRootWindowDrop(object sender, DragEventArgs e)
    {
        _dragDepth = 0;
        ShowDropOverlay(false);

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                await ProcessDroppedStorageItemsAsync(items);
            }
            catch { }
            finally
            {
                deferral.Complete();
            }
        }
    }

    private void ShowDropOverlay(bool show)
    {
        if (_isDropOverlayVisible == show) return;
        _isDropOverlayVisible = show;

        _dropOverlayStoryboard?.Stop();
        _dropOverlayStoryboard = null;

        if (IsReduceMotionEnabled())
        {
            if (show)
            {
                DropOverlayContainer.Opacity = 1.0;
                DropContentScale.ScaleX = 1.0;
                DropContentScale.ScaleY = 1.0;
                DropOverlayContainer.Visibility = Visibility.Visible;
            }
            else
            {
                DropOverlayContainer.Opacity = 0.0;
                DropOverlayContainer.Visibility = Visibility.Collapsed;
            }
            return;
        }

        var sb = new Storyboard();
        var ease = new QuarticEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(show ? 190 : 140);

        var fade = new DoubleAnimation
        {
            From = DropOverlayContainer.Opacity,
            To = show ? 1.0 : 0.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(fade, DropOverlayContainer);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);

        var scaleX = new DoubleAnimation
        {
            From = DropContentScale.ScaleX,
            To = show ? 1.0 : 0.94,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(scaleX, DropContentScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        sb.Children.Add(scaleX);

        var scaleY = new DoubleAnimation
        {
            From = DropContentScale.ScaleY,
            To = show ? 1.0 : 0.94,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTarget(scaleY, DropContentScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        sb.Children.Add(scaleY);

        if (show)
        {
            DropOverlayContainer.Visibility = Visibility.Visible;
        }
        else
        {
            sb.Completed += (s, e) =>
            {
                if (!_isDropOverlayVisible)
                {
                    DropOverlayContainer.Visibility = Visibility.Collapsed;
                    DropOverlayContainer.Opacity = 0.0;
                }
            };
        }

        _dropOverlayStoryboard = sb;
        Helpers.Motion.Begin(sb);
    }

    private async Task ProcessDroppedStorageItemsAsync(IReadOnlyList<IStorageItem> items)
    {
        if (items == null || items.Count == 0) return;

        var videoFilePaths = new List<string>();
        var folderPaths = new List<string>();

        foreach (var item in items)
        {
            if (item is StorageFile file)
            {
                if (NavigationService.IsVideoFilePath(file.Path))
                {
                    videoFilePaths.Add(file.Path);
                }
            }
            else if (item is StorageFolder folder)
            {
                folderPaths.Add(folder.Path);
            }
            else if (!string.IsNullOrEmpty(item.Path))
            {
                if (System.IO.Directory.Exists(item.Path))
                {
                    folderPaths.Add(item.Path);
                }
                else if (System.IO.File.Exists(item.Path) && NavigationService.IsVideoFilePath(item.Path))
                {
                    videoFilePaths.Add(item.Path);
                }
            }
        }

        if (folderPaths.Count > 0)
        {
            var homeVM = App.GetService<HomeViewModel>();
            if (homeVM != null)
            {
                foreach (var folder in folderPaths)
                {
                    await homeVM.AddDirectoryPathAsync(folder);
                }
            }
        }

        if (videoFilePaths.Count > 0)
        {
            await _navigationService.OpenVideoFilesAsync(videoFilePaths);
        }
    }

    private static bool IsReduceMotionEnabled() => Helpers.Motion.IsReduced;
}
