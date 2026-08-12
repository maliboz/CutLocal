using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CutLocal.Contracts;
using CutLocal.Infrastructure;
using CutLocal.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CutLocal.Mac;

/// <summary>Owns the macOS UI lifetime and the shared CutLocal service graph.</summary>
public sealed partial class App : Avalonia.Application
{
    private IHost? _host;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        try
        {
            ApplicationPaths paths = ApplicationPaths.CreateDefault();
            Directory.CreateDirectory(paths.LogRoot);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.WithProperty("Application", "CutLocal")
                .Enrich.WithProperty("Platform", "macOS")
                .WriteTo.File(
                    Path.Combine(paths.LogRoot, "cutlocal-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    formatProvider: CultureInfo.InvariantCulture,
                    shared: false)
                .CreateLogger();

            RegisterGlobalExceptionHandlers();
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddCutLocalCore(paths);
                    services.AddTransient<MacMainWindowViewModel>();
                    services.AddTransient(provider => new MainWindow(
                        provider.GetRequiredService<MacMainWindowViewModel>()));
                })
                .Build();
            _host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = window;
            desktop.Exit += OnDesktopExit;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "CutLocal failed during macOS startup");
            desktop.MainWindow = CreateStartupFailureWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window CreateStartupFailureWindow() => new()
    {
        Title = "CutLocal",
        Width = 540,
        Height = 240,
        Content = new TextBlock
        {
            Margin = new Thickness(32),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "CutLocal başlatılamadı. Ayrıntılar yerel CutLocal log klasörüne yazıldı.\n\n"
                + "CutLocal could not start. Details were written to the local CutLocal log folder.",
        },
    };

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host?.Dispose();
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Log.Fatal(
                eventArgs.ExceptionObject as Exception,
                "Unhandled AppDomain exception; terminating={Terminating}",
                eventArgs.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            Log.Error(eventArgs.Exception, "Unobserved task exception");
    }
}
