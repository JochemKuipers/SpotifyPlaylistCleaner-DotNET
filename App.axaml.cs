using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SpotifyPlaylistCleaner_DotNET.Services;
using SpotifyPlaylistCleaner_DotNET.ViewModels;
using SpotifyPlaylistCleaner_DotNET.Views;

namespace SpotifyPlaylistCleaner_DotNET;

public class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Set up dependency injection
        var services = new ServiceCollection();

        // Register services
        services.AddSingleton<IAuthenticationService, SpotifyAuthenticationService>();
        services.AddSingleton<IDuplicatesService, DuplicatesService>();

        // Change how SpotifyService is registered
        services.AddSingleton<ISpotifyService>(sp => {
            var authService = sp.GetRequiredService<IAuthenticationService>();
            // Initialize with null client but pass the auth service
            return new SpotifyService(null, authService);
        });

        // Register view models
        services.AddSingleton<MainWindowViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}