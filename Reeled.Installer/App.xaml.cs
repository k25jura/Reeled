using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Reeled.Installer.Views;

namespace Reeled.Installer;

public partial class App : Application
{
    public static Window? MainWindowInstance { get; private set; }
    public static bool IsUninstallMode { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cmdArgs = Environment.GetCommandLineArgs();
        IsUninstallMode = cmdArgs.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "/u", StringComparison.OrdinalIgnoreCase));

        bool isSilent = cmdArgs.Any(a => string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(a, "-silent", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(a, "/s", StringComparison.OrdinalIgnoreCase));

        if (IsUninstallMode)
        {
            if (isSilent)
            {
                var installService = new Services.InstallService();
                string installDir = AppDomain.CurrentDomain.BaseDirectory;
                await installService.UninstallAsync(installDir, wipeUserData: false, new Progress<Services.InstallProgress>());
                Environment.Exit(0);
                return;
            }

            var uninstallWindow = new UninstallWindow();
            MainWindowInstance = uninstallWindow;
            uninstallWindow.Activate();
        }
        else
        {
            var mainWindow = new MainWindow();
            MainWindowInstance = mainWindow;
            mainWindow.Activate();
        }
    }
}
