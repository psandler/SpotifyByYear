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
            var spotify = new SpotifyService(new TokenStore());
            var resolver = new ReleaseYearResolver(new ReleaseYearCache(), new MusicBrainzClient(), spotify);
            var viewModel = new MainViewModel(spotify, resolver);

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += (_, _) => viewModel.FlushCaches();
        }

        base.OnFrameworkInitializationCompleted();
    }
}