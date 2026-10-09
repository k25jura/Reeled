using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Reeled.Installer.Views;

namespace Reeled.Installer;

public partial class App : Application
{
    public static Window? MainWindowInstance { get; private set; }
    public static bool IsUninstallMode { get; private set; }
    public static string? TargetInstallDirectory { get; set; }

    public App()
    {
        InitializeComponent();

        UnhandledException += (sender, args) =>
        {
            try
            {
                string log = $"[UnhandledException] {DateTime.Now}\nMessage: {args.Message}\nException: {args.Exception}\nStackTrace:\n{args.Exception?.StackTrace}\n\n";
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "reeled_installer_crash.log"), log);
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            try
            {
                string log = $"[AppDomainUnhandledException] {DateTime.Now}\nException: {args.ExceptionObject}\n\n";
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "reeled_installer_crash.log"), log);
            }
            catch { }
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cmdArgs = Environment.GetCommandLineArgs();
        string currentExeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        IsUninstallMode = cmdArgs.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "/u", StringComparison.OrdinalIgnoreCase)) ||
                          string.Equals(currentExeName, "uninstall", StringComparison.OrdinalIgnoreCase);

        bool isSilent = cmdArgs.Any(a => string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(a, "-silent", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(a, "/s", StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < cmdArgs.Length; i++)
        {
            if (cmdArgs[i].StartsWith("--dir=", StringComparison.OrdinalIgnoreCase))
            {
                TargetInstallDirectory = cmdArgs[i].Substring(6).Trim('"');
            }
            else if (cmdArgs[i].StartsWith("/dir=", StringComparison.OrdinalIgnoreCase))
            {
                TargetInstallDirectory = cmdArgs[i].Substring(5).Trim('"');
            }
            else if (cmdArgs[i].Equals("--dir", StringComparison.OrdinalIgnoreCase) && i + 1 < cmdArgs.Length)
            {
                TargetInstallDirectory = cmdArgs[i + 1].Trim('"');
            }
        }

        if (IsUninstallMode)
        {
            var installService = new Services.InstallService();
            string installDir = TargetInstallDirectory ?? installService.DefaultInstallDirectory;

            if (isSilent)
            {
                await installService.UninstallAsync(installDir, wipeUserData: false, new Progress<Services.InstallProgress>());
                ScheduleTempCleanup();
                Environment.Exit(0);
                return;
            }

            var uninstallWindow = new UninstallWindow();
            MainWindowInstance = uninstallWindow;
            uninstallWindow.ActivateAndBringToForeground();
        }
        else
        {
            var mainWindow = new MainWindow();
            MainWindowInstance = mainWindow;
            mainWindow.Activate();
        }
    }

    public static void ScheduleTempCleanup()
    {
        try
        {
            string currentBase = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string tempPath = Path.GetTempPath().TrimEnd('\\', '/');

            if (currentBase.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(currentBase).StartsWith("Reeled_Uninstall", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 2 /nobreak > NUL & rmdir /s /q \"{currentBase}\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
        }
        catch { }
    }
}
