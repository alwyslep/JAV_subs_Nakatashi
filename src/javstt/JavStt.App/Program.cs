using Avalonia;

namespace JavStt.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // ★Configure<App>, not Configure<Application>: the base class has no MainWindow to create, so
    //   the process starts, stays responsive, and shows nothing at all.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // ★No WithInterFont(): the Avalonia.Fonts.Inter package resolves through avares://
            //   and the font chain is declared in Theme.axaml, same as the fork does it.
            .LogToTrace();
}
