using Avalonia;

namespace CutLocal.Mac;

/// <summary>Starts the native Avalonia desktop lifetime used by the macOS bundle.</summary>
internal static class Program
{
    /// <summary>Starts CutLocal on the current UI thread.</summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>Builds the platform-specific Avalonia application host.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
