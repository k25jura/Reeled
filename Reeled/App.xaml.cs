using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Reeled.Services;
using Reeled.ViewModels;

namespace Reeled;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);
    public static IServiceProvider Services { get; private set; } = null!;

    public static T GetService<T>() where T : class =>
        Services.GetRequiredService<T>();

    public App()
    {
        InitializeComponent();

        UnhandledException += (sender, args) =>
        {
            Helpers.CursorHelper.RestoreGlobalCursor();
            try
            {
                string log = $"[UnhandledException] {DateTime.Now}\nMessage: {args.Message}\nException: {args.Exception}\nStackTrace:\n{args.Exception?.StackTrace}\n\n";
                System.IO.File.AppendAllText("reeled_crash.log", log);
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            Helpers.CursorHelper.RestoreGlobalCursor();
            try
            {
                string log = $"[AppDomain Unhandled] {DateTime.Now}\nExceptionObject: {args.ExceptionObject}\n\n";
                System.IO.File.AppendAllText("reeled_crash.log", log);
            }
            catch { }
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
        {
            Helpers.CursorHelper.RestoreGlobalCursor();
        };

        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core Services
        services.AddSingleton<ILocalStorageService, LocalStorageService>();
        services.AddSingleton<IClipMetadataCacheService, ClipMetadataCacheService>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IClipMetadataService, ClipMetadataService>();
        services.AddSingleton<IClipIndexerService, ClipIndexerService>();
        services.AddSingleton<ILibVlcPlaybackService, LibVlcPlaybackService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IUpdateService, UpdateService>();

        // ViewModels
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var storage = GetService<ILocalStorageService>();
        var loc = GetService<ILocalizationService>();
        loc.SetLanguage(storage.CurrentSettings.Language ?? "System");
        ApplyTheme(storage.CurrentSettings.AppTheme);
        Window.Activate();
    }

    public static bool IsWindowsInLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
            {
                return val != 0;
            }
        }
        catch { }

        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var bg = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return (bg.R + bg.G + bg.B) > 384;
        }
        catch { }

        return false;
    }

    public static void ApplyTheme(string themeSetting)
    {
        if (Window?.Content is FrameworkElement root)
        {
            ElementTheme targetTheme = themeSetting switch
            {
                "Dark" => ElementTheme.Dark,
                "Light" => ElementTheme.Light,
                _ => IsWindowsInLightTheme() ? ElementTheme.Light : ElementTheme.Dark
            };

            root.RequestedTheme = targetTheme;

            if (Window is MainWindow mainWindow)
            {
                mainWindow.NavigationFrame.RequestedTheme = targetTheme;
                if (mainWindow.NavigationFrame.Content is FrameworkElement page)
                {
                    page.RequestedTheme = targetTheme;
                    if (page is Views.SettingsPage settingsPage && settingsPage.FindName("SettingsScrollViewer") is FrameworkElement sv)
                    {
                        sv.RequestedTheme = targetTheme;
                    }
                    if (page is Views.HomePage homePage && homePage.FindName("ClipsGridView") is FrameworkElement gv)
                    {
                        gv.RequestedTheme = targetTheme;
                    }
                }

                if (mainWindow.IsPlayerVisible)
                {
                    mainWindow.UpdateTitleBarTheme(ElementTheme.Dark);
                }
                else
                {
                    mainWindow.UpdateTitleBarTheme(targetTheme);
                }
            }
        }
    }
}
