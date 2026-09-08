using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
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
    private readonly SUBCLASSPROC _subclassProc;

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

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = (dpi > 0 ? dpi : 96) / 96.0;

            mmi.ptMinTrackSize.x = (int)(MinWindowWidth * scale);
            mmi.ptMinTrackSize.y = (int)(MinWindowHeight * scale);

            Marshal.StructureToPtr(mmi, lParam, true);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
