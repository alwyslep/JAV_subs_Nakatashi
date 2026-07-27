using Avalonia.Media;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Theming.Nakatashi;

namespace Nikse.SubtitleEdit.Logic.Plugins;

/// <summary>
/// Builds <see cref="PluginThemeColors"/> for the currently active Subtitle Edit theme.
/// Dark colors come from <see cref="SeAppearance"/>; Classic/Pastel/Light use the same
/// hard-coded palettes as <see cref="UiTheme"/>.
/// </summary>
internal static class PluginThemeColorsFactory
{
    public static PluginThemeColors Build()
    {
        var appearance = Se.Settings.Appearance;
        var themeName = UiTheme.ThemeName;
        // Fork (Nakatashi): route through the shared predicate so plugins see Nakatashi as dark;
        // the theme-aware getters below return the Nakatashi palette while it is active.
        var isDark = UiTheme.IsDarkThemeEnabled();

        Color background;
        Color foreground;

        if (isDark)
        {
            background = UiTheme.GetDarkThemeBackgroundColor();
            foreground = UiTheme.GetDarkThemeForegroundColor();
        }
        else if (themeName == UiTheme.ThemeNameClassic)
        {
            background = Color.FromRgb(236, 233, 216);
            foreground = Color.FromRgb(0, 0, 0);
        }
        else if (themeName == UiTheme.ThemeNamePastel)
        {
            background = Color.FromRgb(240, 235, 255);
            foreground = Color.FromRgb(0, 0, 0);
        }
        else
        {
            background = Color.FromRgb(255, 255, 255);
            foreground = Color.FromRgb(0, 0, 0);
        }

        return new PluginThemeColors
        {
            IsDark = isDark,
            BackgroundColor = background.FromColorToHex(),
            ForegroundColor = foreground.FromColorToHex(),
            // Fork (Nakatashi): the active accent is the theme gradient, which a single hex
            // cannot express - export its middle stop so plugin UIs stay on-brand. (The
            // focused-button hex is no longer painted anywhere while Nakatashi is active.)
            AccentColor = NakatashiTheme.TryGetPalette(UiTheme.ThemeName, out var nakatashiPalette)
                ? nakatashiPalette.AccentMid.FromColorToHex()
                : appearance.FocusedButtonBackgroundColor,
            BackgroundColorLighter = UiUtil.LightenColor(background, 5).FromColorToHex(),
            BackgroundColorHeader = UiUtil.LightenColor(background, 15).FromColorToHex(),
            BookmarkColor = appearance.BookmarkColor,
        };
    }
}
