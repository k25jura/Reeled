using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Reeled.Installer.Services;
using Windows.Graphics;

namespace Reeled.Installer.Views;

public sealed partial class UninstallWindow : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly InstallService _installService = new();
    private AppWindow? _appWindow;
    private bool _isUninstallCompleted = false;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_SHOWWINDOW = 0x40;

    public UninstallWindow()
    {
        InitializeComponent();

        ConfigureWindow();
        ApplyLocalization();

        RootGrid.Loaded += (s, e) =>
        {
            UpdateTitleBarTheme(RootGrid.ActualTheme);
            BringToFront();
        };
    }

    public void ActivateAndBringToForeground()
    {
        Activate();
        BringToFront();
    }

    /// <summary>
    /// The uninstaller is started by WMI (outside Settings' job), so Windows doesn't count it as launched
    /// by the user and keeps it behind. The stager hands its foreground right over; if that hasn't
    /// arrived, join the foreground thread's input queue, which lets SetForegroundWindow through, and
    /// pass through topmost so the window is at least on top of Settings.
    /// </summary>
    private void BringToFront()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, 9); // SW_RESTORE
            if (GetForegroundWindow() == hwnd) return;

            if (!SetForegroundWindow(hwnd))
            {
                uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
                uint thisThread = GetCurrentThreadId();
                bool attached = foregroundThread != 0 && foregroundThread != thisThread &&
                                AttachThreadInput(thisThread, foregroundThread, true);
                try
                {
                    SetForegroundWindow(hwnd);
                }
                finally
                {
                    if (attached) AttachThreadInput(thisThread, foregroundThread, false);
                }
            }

            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            BringWindowToTop(hwnd);
            Program.Trace($"Uninstall window shown, foreground={GetForegroundWindow() == hwnd}");
        }
        catch { }
    }

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (_appWindow != null)
        {
            _appWindow.Title = _loc["Uninstall_Title"];
            _appWindow.Resize(new SizeInt32(600, 390));
            CenterWindowOnScreen(600, 390);

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }

            _appWindow.Closing += (s, e) =>
            {
                App.ScheduleTempCleanup();
                Environment.Exit(_isUninstallCompleted ? 0 : 1);
            };

            UpdateTitleBarTheme(RootGrid.ActualTheme);
        }

        try
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch { }
    }

    private void CenterWindowOnScreen(int width, int height)
    {
        if (_appWindow == null) return;

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
            if (displayArea != null)
            {
                var workArea = displayArea.WorkArea;
                int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
                int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
                _appWindow.Move(new PointInt32(x, y));
            }
        }
        catch { }
    }

    private void UpdateTitleBarTheme(ElementTheme theme)
    {
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && _appWindow?.TitleBar != null)
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            if (theme == ElementTheme.Dark)
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
    }

    private void ApplyLocalization()
    {
        if (_appWindow != null)
        {
            _appWindow.Title = _loc["Uninstall_Title"];
        }

        TitleText.Text = _loc["Uninstall_Confirm_Title"];
        SubtitleText.Text = _loc["Uninstall_Confirm_Message"];
        WipeUserDataCheckBox.Content = _loc["Uninstall_WipeData"];

        DoneTitleText.Text = _loc["Uninstall_Finished_Title"];
        DoneSubtitleText.Text = _loc["Uninstall_Finished_Message"];

        UninstallButton.Content = _loc["Uninstall_Btn_Uninstall"];
        CancelButton.Content = _loc["Btn_Cancel"];
        CloseButton.Content = _loc["Uninstall_Btn_Close"];
    }

    private async void OnUninstallClicked(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        UninstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        bool wipe = WipeUserDataCheckBox.IsChecked == true;
        string installDir = App.TargetInstallDirectory ?? _installService.DefaultInstallDirectory;

        var progress = new Progress<InstallProgress>(p =>
        {
            UninstallProgressBar.Value = p.Percentage;
            ProgressStatusText.Text = p.StatusText;
        });

        try
        {
            await _installService.UninstallAsync(installDir, wipe, progress);
        }
        catch (Exception ex)
        {
            ProgressStatusText.Text = $"Error: {ex.Message}";
        }

        ProgressPanel.Visibility = Visibility.Collapsed;
        FinishedPanel.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        UninstallButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        _isUninstallCompleted = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        App.ScheduleTempCleanup();
        Environment.Exit(1);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        App.ScheduleTempCleanup();
        Environment.Exit(0);
    }
}
