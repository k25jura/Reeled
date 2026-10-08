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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cmdArgs = Environment.GetCommandLineArgs();
        IsUninstallMode = cmdArgs.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase));

        if (IsUninstallMode)
        {
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
