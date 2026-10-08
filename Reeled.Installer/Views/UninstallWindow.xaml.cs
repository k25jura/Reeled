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
    }

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {
            _appWindow.Title = _loc["Uninstall_Title"];
            _appWindow.Resize(new SizeInt32(560, 360));

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
        }

        try
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch { }
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
        string installDir = AppDomain.CurrentDomain.BaseDirectory;

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
        this.Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
