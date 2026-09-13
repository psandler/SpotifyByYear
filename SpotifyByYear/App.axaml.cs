using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SpotifyByYear.Services;
using SpotifyByYear.ViewModels;
using SpotifyByYear.Views;

namespace SpotifyByYear;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new SpotifyService(new TokenStore())),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}