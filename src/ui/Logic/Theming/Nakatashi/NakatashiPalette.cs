using Avalonia.Media;

namespace Nikse.SubtitleEdit.Logic.Theming.Nakatashi;

/// <summary>
/// Fork-only (Nakatashi): the swappable color table for one "Deep Gray" theme variant.
/// Unlike the stock Dark theme, whose colors are user-configurable settings
/// (<see cref="Config.SeAppearance.DarkModeBackgroundColor"/>), a Nakatashi palette is a
/// fixed, designed set - both variants ship side by side and the user picks one in the
/// theme dropdown. Layering model (darkest to brightest): Base (window) → Surface
/// (inputs/cards) → Elevated (menus/popups) → Header (grid headers). Borders are
/// near-invisible white with very low alpha, per the Deep Gray design reference.
/// </summary>
public sealed class NakatashiPalette
{
    /// <summary>The theme name stored in settings and shown in the theme dropdown.</summary>
    public required string ThemeName { get; init; }

    /// <summary>Deepest layer - the window/region background.</summary>
    public required Color Base { get; init; }

    /// <summary>One step up from Base - text boxes, cards, input surfaces.</summary>
    public required Color Surface { get; init; }

    /// <summary>Hover state for Surface-colored controls.</summary>
    public required Color SurfaceHover { get; init; }

    /// <summary>Pressed state for Surface-colored controls.</summary>
    public required Color SurfacePressed { get; init; }

    /// <summary>Floating layer - menus, context menus, flyouts, popups.</summary>
    public required Color Elevated { get; init; }

    /// <summary>Grid/list column headers - the brightest opaque layer.</summary>
    public required Color Header { get; init; }

    /// <summary>Primary text. Not pure white - slightly dimmed per the design reference.</summary>
    public required Color TextPrimary { get; init; }

    /// <summary>Secondary/help text - white at ~60% alpha. Reserved for later phases.</summary>
    public required Color TextSecondary { get; init; }

    /// <summary>Near-invisible hairline (white/5) - resting borders, separators.</summary>
    public required Color BorderSubtle { get; init; }

    /// <summary>Slightly stronger border (white/10) - interactive control borders.</summary>
    public required Color BorderStrong { get; init; }

    /// <summary>
    /// Gemini-glow accent gradient stops (left → mid → right), from the Deep Gray reference:
    /// blue #4a77ff → purple #a374ff → coral #ff8a75. Painted only on ACTIVE elements
    /// (focus rings, selection, menu highlight) - never on resting surfaces.
    /// </summary>
    public required Color AccentStart { get; init; }

    public required Color AccentMid { get; init; }

    public required Color AccentEnd { get; init; }

    /// <summary>
    /// Darker warm terminus for FILL surfaces that carry primary text (selected rows/items).
    /// Measured: #FF8A75 at the stock 0.6 selection opacity over Base composites to ~4.2:1
    /// against TextPrimary - below the WCAG AA 4.5:1 floor; this stop lands ~5.2:1.
    /// Rings/borders keep <see cref="AccentEnd"/>, where the full coral measures strongest.
    /// </summary>
    public required Color AccentEndFill { get; init; }

    /// <summary>
    /// Claude-desktop style: refined neutral charcoal, no color tint.
    /// Reference: main #191919, containers #222222.
    /// </summary>
    public static readonly NakatashiPalette Charcoal = new()
    {
        ThemeName = "Nakatashi Charcoal",
        Base = Color.FromRgb(0x19, 0x19, 0x19),
        Surface = Color.FromRgb(0x22, 0x22, 0x22),
        SurfaceHover = Color.FromRgb(0x28, 0x28, 0x28),
        SurfacePressed = Color.FromRgb(0x1E, 0x1E, 0x1E),
        Elevated = Color.FromRgb(0x2A, 0x2A, 0x2A),
        Header = Color.FromRgb(0x2E, 0x2E, 0x2E),
        TextPrimary = Color.FromRgb(0xEC, 0xEC, 0xEC),
        TextSecondary = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF),
        BorderSubtle = Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF),
        BorderStrong = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
        AccentStart = Color.FromRgb(0x4A, 0x77, 0xFF),
        AccentMid = Color.FromRgb(0xA3, 0x74, 0xFF),
        AccentEnd = Color.FromRgb(0xFF, 0x8A, 0x75),
        AccentEndFill = Color.FromRgb(0xD9, 0x70, 0x5C),
    };

    /// <summary>
    /// Gemini style: deep night sky with a faint cool blue tint.
    /// Reference: main #131314, containers #1e1f20.
    /// </summary>
    public static readonly NakatashiPalette CoolBlue = new()
    {
        ThemeName = "Nakatashi Blue",
        Base = Color.FromRgb(0x13, 0x13, 0x14),
        Surface = Color.FromRgb(0x1E, 0x1F, 0x20),
        SurfaceHover = Color.FromRgb(0x24, 0x25, 0x26),
        SurfacePressed = Color.FromRgb(0x1A, 0x1B, 0x1C),
        Elevated = Color.FromRgb(0x28, 0x2A, 0x2C),
        Header = Color.FromRgb(0x2C, 0x2E, 0x30),
        TextPrimary = Color.FromRgb(0xE3, 0xE3, 0xE3),
        TextSecondary = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF),
        BorderSubtle = Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF),
        BorderStrong = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
        AccentStart = Color.FromRgb(0x4A, 0x77, 0xFF),
        AccentMid = Color.FromRgb(0xA3, 0x74, 0xFF),
        AccentEnd = Color.FromRgb(0xFF, 0x8A, 0x75),
        AccentEndFill = Color.FromRgb(0xD9, 0x70, 0x5C),
    };
}
