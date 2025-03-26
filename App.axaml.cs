using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DotNetEnv;
using SpotifyPlaylistCleaner_DotNET.ViewModels;
using SpotifyPlaylistCleaner_DotNET.Views;

namespace SpotifyPlaylistCleaner_DotNET;

public class App : Application
{
    public override void Initialize()
    {
        Env.Load();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };

        base.OnFrameworkInitializationCompleted();
    }
}