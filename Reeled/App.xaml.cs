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
            try
            {
                string log = $"[AppDomain Unhandled] {DateTime.Now}\nExceptionObject: {args.ExceptionObject}\n\n";
                System.IO.File.AppendAllText("reeled_crash.log", log);
            }
            catch { }
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

        // ViewModels
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
    }

    public static void ApplyTheme(string themeSetting)
    {
        if (Window?.Content is FrameworkElement root)
        {
            ElementTheme theme = themeSetting switch
            {
                "Dark" => ElementTheme.Dark,
                "Light" => ElementTheme.Light,
                _ => ElementTheme.Default
            };
            root.RequestedTheme = theme;
            if (Window is MainWindow mainWindow)
            {
                if (mainWindow.IsPlayerVisible)
                {
                    mainWindow.UpdateTitleBarTheme(ElementTheme.Dark);
                }
                else
                {
                    mainWindow.UpdateTitleBarTheme(root.ActualTheme);
                }
            }
        }
    }
}
