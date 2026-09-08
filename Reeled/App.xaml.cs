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
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core Services
        services.AddSingleton<ILocalStorageService, LocalStorageService>();
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
}
