using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using JavStt.Core;
using Nikse.SubtitleEdit.Logic.Theming.Nakatashi;

namespace JavStt.App;

public class App : Application
{
    /// <summary>Which of the two shipped Deep Gray variants this tool wears.</summary>
    public static readonly NakatashiPalette Palette = NakatashiPalette.Charcoal;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyPalette();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = JavSttSettings.Load();
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(settings) };
            desktop.ShutdownRequested += (_, _) => settings.Save();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Pushes the linked colour table into the resource keys Theme.axaml paints with.
    ///
    /// ★Done in code rather than as literals in the axaml so the palette stays the single source
    ///   of truth. It is a linked file from the fork - if a colour is retuned there, this tool
    ///   follows without anyone remembering to copy a hex value.
    /// </summary>
    private static void ApplyPalette()
    {
        var p = Palette;
        Set("NkBase", p.Base);
        Set("NkSurface", p.Surface);
        Set("NkSurfaceHover", p.SurfaceHover);
        Set("NkSurfacePressed", p.SurfacePressed);
        Set("NkElevated", p.Elevated);
        Set("NkHeader", p.Header);
        Set("NkTextPrimary", p.TextPrimary);
        Set("NkTextSecondary", p.TextSecondary);
        Set("NkBorderSubtle", p.BorderSubtle);
        Set("NkBorderStrong", p.BorderStrong);
        Set("NkAccentStart", p.AccentStart);
        Set("NkAccentMid", p.AccentMid);
        Set("NkAccentEnd", p.AccentEnd);
        Set("NkAccentEndFill", p.AccentEndFill);

        // The Gemini-glow gradient, painted only on active things - progress and the primary
        // button. Never on a resting surface, per the design reference the fork follows.
        Current!.Resources["NkAccentGradient"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(p.AccentStart, 0),
                new GradientStop(p.AccentMid, 0.5),
                new GradientStop(p.AccentEnd, 1),
            },
        };
    }

    private static void Set(string key, Color color)
    {
        Current!.Resources[key] = color;
        Current!.Resources[key + "Brush"] = new SolidColorBrush(color);
    }
}
