using Avalonia;

namespace SysmlStudio.App;

/// <summary>The entry point. The folder to open can be given as the one argument.</summary>
internal static class Program
{
    /// <summary>The model folder named on the command line, if there was one.</summary>
    public static string? StartupFolder { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartupFolder = args.Length > 0 ? args[0] : null;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
