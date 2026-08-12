using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CutLocal.Contracts;
using CutLocal.Infrastructure;
using CutLocal.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CutLocal.App;

/// <summary>Owns the WPF lifetime and dependency-injection host.</summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ApplicationPaths paths = ApplicationPaths.CreateDefault();
        Directory.CreateDirectory(paths.LogRoot);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "CutLocal")
            .Enrich.WithProperty("Runtime", Environment.Version.ToString())
            .Enrich.WithProperty("OperatingSystem", Environment.OSVersion.VersionString)
            .WriteTo.File(
                Path.Combine(paths.LogRoot, "cutlocal-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: CultureInfo.InvariantCulture,
                shared: false)
            .CreateLogger();

        RegisterGlobalExceptionHandlers();

        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddCutLocalCore(paths);
                    services.AddSingleton<IFileDialogService, FileDialogService>();
                    services.AddSingleton<IClipboardService, ClipboardService>();
                    services.AddSingleton<IPreviewBitmapService, PreviewBitmapService>();
                    services.AddSingleton<IFileLauncher, FileLauncher>();
                    services.AddSingleton<ILocalizationService, LocalizationService>();
                    services.AddSingleton<ModelManagerViewModel>();
                    services.AddSingleton<IModelManagerDialog, ModelManagerDialog>();
                    services.AddTransient<BatchWorkspaceViewModel>();
                    services.AddTransient<MainWindowViewModel>();
                    services.AddTransient<MainWindow>();
                })
                .Build();
            await _host.StartAsync(CancellationToken.None);
            Log.Information("Activating bundled models");
            int seededModels = await _host.Services
                .GetRequiredService<IBundledModelSeeder>()
                .SeedAsync(CancellationToken.None);
            Log.Information("Bundled model activation completed; seeded={SeededModels}", seededModels);

            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
            Log.Information("Main window shown");
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "CutLocal failed during startup");
            MessageBox.Show(
                "CutLocal başlatılamadı. Ayrıntılar yerel log dosyasına yazıldı.\n\n"
                + "CutLocal could not start. Details were written to the local log.",
                "CutLocal",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host?.Dispose();
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled WPF dispatcher exception");
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled AppDomain exception; terminating={Terminating}", e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
    }
}
