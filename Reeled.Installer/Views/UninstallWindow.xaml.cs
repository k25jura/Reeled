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

    public UninstallWindow()
    {
        InitializeComponent();

        ConfigureWindow();
        ApplyLocalization();

        RootGrid.Loaded += (s, e) =>
        {
            UpdateTitleBarTheme(RootGrid.ActualTheme);
        };
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
            _appWindow.Resize(new SizeInt32(580, 380));
            CenterWindowOnScreen(580, 380);

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
                Environment.Exit(0);
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
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        App.ScheduleTempCleanup();
        Environment.Exit(0);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        App.ScheduleTempCleanup();
        Environment.Exit(0);
    }
}
