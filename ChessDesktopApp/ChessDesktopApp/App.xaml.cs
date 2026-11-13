using ChessDesktopApp.Engine;
using ChessDesktopApp.Services;
using ChessDesktopApp.ViewModels;
using ChessDesktopApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChessDesktopApp;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private Window? _window;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            LogException(eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject?.ToString()));
        InitializeComponent();
        UnhandledException += (_, eventArgs) => LogException(eventArgs.Exception);
        var services = new ServiceCollection();
        services.AddSingleton<JsonFileStore>();
        services.AddSingleton<EngineRepository>();
        services.AddSingleton<EngineGameRepository>();
        services.AddSingleton<TournamentRepository>();
        services.AddSingleton<EngineManager>();
        services.AddSingleton<EngineUsageCoordinator>();
        services.AddSingleton<EngineMatchManager>();
        services.AddSingleton<TournamentManager>();
        services.AddSingleton<GameSessionService>(provider =>
            new GameSessionService(
                provider.GetRequiredService<EngineManager>(),
                provider.GetRequiredService<EngineUsageCoordinator>(),
                provider.GetRequiredService<EngineGameRepository>()));
        services.AddSingleton<GameViewModel>();
        services.AddSingleton<EngineHubViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = _services.GetRequiredService<MainWindow>();
        }
        catch (Exception exception)
        {
            LogException(exception);
            _window = CreateStartupErrorWindow(exception);
        }

        _window.Activate();
    }

    private static Window CreateStartupErrorWindow(Exception exception)
    {
        var window = new Window { Title = "Fluent Chess — błąd uruchamiania" };
        window.Content = new Grid
        {
            Padding = new Thickness(32),
            Children =
            {
                new StackPanel
                {
                    Spacing = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Nie udało się otworzyć głównego okna", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                        new TextBlock
                        {
                            Text = exception.Message,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 640
                        },
                        new TextBlock
                        {
                            Text = $"Szczegóły zapisano w: {GetLogPath()}",
                            TextWrapping = TextWrapping.Wrap,
                            Opacity = 0.7
                        }
                    }
                }
            }
        };
        return window;
    }

    private static void LogException(Exception exception)
    {
        try
        {
            var path = GetLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static string GetLogPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentChess", "crash.log");
}
