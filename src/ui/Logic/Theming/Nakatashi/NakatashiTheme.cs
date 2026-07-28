using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Logic.Theming.Nakatashi;

/// <summary>
/// Fork-only (Nakatashi): applies one of the "Deep Gray" theme variants
/// (<see cref="NakatashiPalette.Charcoal"/> / <see cref="NakatashiPalette.CoolBlue"/>).
///
/// Lives alongside the stock themes - it never replaces <c>UiTheme.ApplyLighterDark()</c>;
/// <c>UiTheme.SetCurrentTheme()</c> dispatches to exactly one of them. The style set below is
/// modeled on ApplyLighterDark (same control coverage, including its battle-tested
/// ComboBox/PART_BorderElement guards) with three deliberate differences:
/// 1. Colors come from the fixed <see cref="NakatashiPalette"/>, not the user-configurable
///    DarkMode* settings, so Dark and Nakatashi stay decoupled.
/// 2. Layered surfaces: window Base → input Surface → menu/popup Elevated → grid Header,
///    with near-invisible white borders instead of hard lines.
/// 3. Fluent resource overrides round the corners app-wide (ControlCornerRadius 8,
///    OverlayCornerRadius 12) and restyle stateful controls (Button, menus) through the
///    resource keys their templates already consume - unknown keys are harmless no-ops.
///
/// Backdrop blur (Deep Gray reference doc): resolved as NOT available in-app on Avalonia
/// 12.1 - TransparencyLevelHint/ExperimentalAcrylicBorder only blur the desktop behind the
/// window, and Visual.Effect blurs an element's own subtree, not what is beneath it. Later
/// phases use translucent Elevated surfaces (and, for modals over frozen content, an
/// optional RenderTargetBitmap + BlurEffect snapshot); Phase 1 sticks to opaque layers.
/// </summary>
public static class NakatashiTheme
{
    private static Styles? _styles;
    private static ResourceDictionary? _resources;

    public const string ThemeNameCharcoal = "Nakatashi Charcoal";
    public const string ThemeNameBlue = "Nakatashi Blue";

    /// <summary>
    /// Phase 2 typography chain: Latin → Inter (from the Avalonia.Fonts.Inter package the app
    /// already ships; upstream only registers it as the default on Linux because Inter alone
    /// would break CJK on Windows), Hangul/kana → embedded Pretendard, CJK ideographs →
    /// embedded Pretendard JP (base Pretendard has kana but NO kanji - without the JP sibling,
    /// Japanese subtitle lines would mix Pretendard kana with an OS-fallback face for kanji),
    /// and $Default last so symbols and scripts none of these cover keep the OS fallback chain.
    /// Kana can still split between the KR/JP siblings across runs; they are the same design,
    /// so the seam is invisible in practice.
    /// </summary>
    public const string UiFontChain =
        "avares://Avalonia.Fonts.Inter/Assets#Inter, avares://SubtitleEdit/Assets/Fonts#Pretendard, avares://SubtitleEdit/Assets/Fonts#Pretendard JP, $Default";

    /// <summary>
    /// True when the app font setting means "no explicit user choice". Three spellings exist:
    /// empty, the dropdown literal "Default", and "$Default" (FontFamily.DefaultFontFamilyName) -
    /// upstream's SettingsViewModel persists <c>new Label().FontFamily.Name</c> == "$Default"
    /// whenever the dropdown shows Default, so a naive == "Default" check would fail on every
    /// settings file that has ever been saved. (macOS defaults to "Helvetica Neue", an explicit
    /// choice - the chain intentionally never activates there.)
    /// </summary>
    public static bool IsDefaultFontSentinel(string? fontName)
    {
        return string.IsNullOrEmpty(fontName)
               || fontName == "Default"
               || fontName == FontFamily.DefaultFontFamilyName;
    }

    /// <summary>
    /// True when the Nakatashi typography chain should reach the subtitle grid/edit box - i.e.
    /// the sites that upstream pins with a local FontFamily("Default") may skip that pin.
    /// All three conditions must hold: the subtitle font setting is unset, a Nakatashi theme is
    /// active, and the app font is unset (an explicit app font must NOT leak into the subtitle
    /// surfaces - upstream's local pin shielded them from SetFontName's app-wide styles, and
    /// that shielding must survive on stock themes and explicit-font setups).
    /// </summary>
    public static bool OwnsSubtitleFont(string? subtitleFontName)
    {
        return IsDefaultFontSentinel(subtitleFontName)
               && IsNakatashiThemeName(Se.Settings.Appearance.Theme)
               && IsDefaultFontSentinel(Se.Settings.Appearance.FontName);
    }

    /// <summary>
    /// The subtitle-surface font for the few sites that assign a FontFamily <em>unconditionally</em>
    /// (a ternary onto a bound property, say). Those cannot be fixed by skipping the pin the way
    /// the <see cref="OwnsSubtitleFont"/> guard does elsewhere: their else-branch is a hard
    /// <c>FontFamily.Default</c>, so the surface would still render the OS face. They need the
    /// chain handed to them. Returns exactly what upstream would have used whenever Nakatashi
    /// does not own the surface.
    /// </summary>
    public static FontFamily SubtitleSurfaceFont(string? subtitleFontName)
    {
        if (OwnsSubtitleFont(subtitleFontName))
        {
            return new FontFamily(UiFontChain);
        }

        return string.IsNullOrEmpty(subtitleFontName)
            ? FontFamily.Default
            : new FontFamily(subtitleFontName);
    }

    /// <summary>
    /// Set while a Nakatashi theme is active: UiUtil.GetFocusedButtonBackgroundBrush returns
    /// this instead of the user's translucent-blue fill, so every dialog's focused button
    /// speaks the theme's accent-gradient language. Null while any other theme is active.
    /// The per-button Button:focus style UiUtil.MakeButton installs outranks app-level theme
    /// styles, which is why this hooks the brush getter rather than adding a style.
    /// </summary>
    public static IBrush? FocusedButtonBrush { get; private set; }

    private static Color WithAlpha(Color c, byte a)
    {
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    /// <summary>
    /// The Gemini-glow gradient (AccentStart → AccentMid → AccentEnd, left to right).
    /// <paramref name="alpha"/> bakes translucency into the stops (for subtle tints);
    /// <paramref name="opacity"/> mirrors how Fluent's stock selection brushes carry
    /// their dimming in Brush.Opacity (0.6 low / 0.8 medium), so overrides keep the
    /// same perceived strength as the accent brushes they replace.
    /// </summary>
    private static LinearGradientBrush MakeGradient(Color start, Color mid, Color end, byte alpha, double opacity)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            Opacity = opacity,
            GradientStops =
            {
                new GradientStop(WithAlpha(start, alpha), 0),
                new GradientStop(WithAlpha(mid, alpha), 0.5),
                new GradientStop(WithAlpha(end, alpha), 1),
            },
        };
    }

    private static LinearGradientBrush MakeAccentGradient(NakatashiPalette p, byte alpha = 0xFF, double opacity = 1.0)
    {
        return MakeGradient(p.AccentStart, p.AccentMid, p.AccentEnd, alpha, opacity);
    }

    /// <summary>Fill-surface variant: darker warm end for AA text contrast (see AccentEndFill).</summary>
    private static LinearGradientBrush MakeAccentFillGradient(NakatashiPalette p, byte alpha = 0xFF, double opacity = 1.0)
    {
        return MakeGradient(p.AccentStart, p.AccentMid, p.AccentEndFill, alpha, opacity);
    }

    public static bool IsNakatashiThemeName(string? themeName)
    {
        return themeName == ThemeNameCharcoal || themeName == ThemeNameBlue;
    }

    public static bool TryGetPalette(string? themeName, out NakatashiPalette palette)
    {
        if (themeName == ThemeNameCharcoal)
        {
            palette = NakatashiPalette.Charcoal;
            return true;
        }

        if (themeName == ThemeNameBlue)
        {
            palette = NakatashiPalette.CoolBlue;
            return true;
        }

        palette = NakatashiPalette.Charcoal;
        return false;
    }

    internal static void Apply(NakatashiPalette p)
    {
        if (Application.Current == null)
        {
            return;
        }

        // The theme-aware UiTheme.GetDarkThemeBackgroundColor() returns p.Base while a
        // Nakatashi theme is active, so this paints the Fluent region (window background).
        UiTheme.UpdateRegionColor();

        var foregroundBrush = new SolidColorBrush(p.TextPrimary);
        var surfaceBrush = new SolidColorBrush(p.Surface);
        var surfaceHoverBrush = new SolidColorBrush(p.SurfaceHover);
        var surfacePressedBrush = new SolidColorBrush(p.SurfacePressed);

        // Claude-desktop translucent floating layer. Real backdrop blur is impossible in-app
        // (class doc), so popups use per-pixel alpha over whatever is beneath instead - popup
        // windows composite alpha by default (Fluent's PopupRoot requests Transparent, and the
        // rounded menu corners already rely on it). 0xE0 (~88%) keeps text ≥7:1 contrast even
        // when a menu overhangs the window onto bright content.
        var elevatedBrush = new SolidColorBrush(Color.FromArgb(0xE0, p.Elevated.R, p.Elevated.G, p.Elevated.B));
        var elevatedOpaqueBrush = new SolidColorBrush(p.Elevated);
        var headerBrush = new SolidColorBrush(p.Header);
        var borderSubtleBrush = new SolidColorBrush(p.BorderSubtle);
        var borderStrongBrush = new SolidColorBrush(p.BorderStrong);

        // Full coral end = rings/borders/pipes only; fill surfaces under primary text use the
        // darker AccentEndFill terminus (AA - see the palette doc). Brush.Opacity mirrors the
        // stock accent brushes' built-in 0.6/0.8 selection dimming.
        var accentGradient = MakeAccentGradient(p);
        var accentMenuTint = MakeAccentGradient(p, 0x30);
        var accentMenuTintPressed = MakeAccentGradient(p, 0x48);
        var accentFill = MakeAccentFillGradient(p);
        var accentFillLow = MakeAccentFillGradient(p, opacity: 0.6);
        var accentFillMedium = MakeAccentFillGradient(p, opacity: 0.8);
        var accentFillFaint = MakeAccentFillGradient(p, opacity: 0.25);

        // TextBox selection: a relative gradient restarts per selection rectangle (looks broken
        // across wrapped lines), so selection is a translucent solid accent instead.
        var selectionHighlightBrush = new SolidColorBrush(WithAlpha(p.AccentMid, 0x59));

        // ToggleButton :checked (waveform toolbar pills): stock Fluent paints the raw OS
        // personalization accent (an arbitrary user color); a gradient on a ~24px pill reads
        // as mush, so checked state is the solid theme accent.
        var toggleCheckedBrush = new SolidColorBrush(p.AccentStart);
        var toggleCheckedHoverBrush = new SolidColorBrush(UiUtil.LightenColor(p.AccentStart, 24));
        var toggleCheckedPressedBrush = new SolidColorBrush(Color.FromRgb(
            (byte)(p.AccentStart.R * 3 / 4), (byte)(p.AccentStart.G * 3 / 4), (byte)(p.AccentStart.B * 3 / 4)));

        FocusedButtonBrush = MakeAccentGradient(p, 0x46);

        _resources = new ResourceDictionary
        {
            // Deep Gray look: generous radii app-wide. Fluent defaults are 3/8; every stock
            // template picks these up via DynamicResource, no per-control styling needed.
            ["ControlCornerRadius"] = new CornerRadius(8),
            ["OverlayCornerRadius"] = new CornerRadius(12),

            // Same guard as ApplyLighterDark: keep text-control foreground stable on
            // focus/hover instead of Fluent's white flash.
            ["TextControlForeground"] = foregroundBrush,
            ["TextControlForegroundPointerOver"] = foregroundBrush,
            ["TextControlForegroundFocused"] = foregroundBrush,
            ["TextControlForegroundDisabled"] = foregroundBrush,

            // Buttons are stateful (hover/pressed swap resources), so restyle them through
            // the resource keys their Fluent template consumes rather than hard setters.
            ["ButtonBackground"] = surfaceBrush,
            ["ButtonBackgroundPointerOver"] = surfaceHoverBrush,
            ["ButtonBackgroundPressed"] = surfacePressedBrush,
            ["ButtonBorderBrush"] = borderSubtleBrush,
            ["ButtonBorderBrushPointerOver"] = borderStrongBrush,
            ["ButtonBorderBrushPressed"] = borderStrongBrush,

            // Menu popups float on the (translucent) Elevated layer with a hairline edge.
            ["MenuFlyoutPresenterBackground"] = elevatedBrush,
            ["MenuFlyoutPresenterBorderBrush"] = borderSubtleBrush,

            // ComboBox dropdowns share the floating layer; tooltips sit directly over the
            // exact text they explain, so pin them opaque.
            ["ComboBoxDropDownBackground"] = elevatedBrush,
            ["ToolTipBackground"] = elevatedOpaqueBrush,

            // ---- Gemini-glow accent: ACTIVE elements only (focus/selection/menu highlight).
            // Every key below lands on a Border/ContentPresenter/Rectangle via DynamicResource,
            // all of which accept a gradient IBrush (recon-verified against Fluent 12.1).

            // Focused TextBox border (TextBox nulls its FocusAdorner - this IS the focus visual).
            ["TextControlBorderBrushFocused"] = accentGradient,

            // ComboBox keyboard-focus ring; the second key is Fluent's accent flood-fill behind
            // it, a parse-time alias that must be overridden by its own name or it drowns the ring.
            ["ComboBoxBackgroundBorderBrushFocused"] = accentGradient,
            ["ComboBoxBackgroundUnfocused"] = Brushes.Transparent,

            // App-wide keyboard-focus adorner (Button, CheckBox, ListBoxItem...): one 2px
            // gradient ring instead of Fluent's black+white double outline.
            ["SystemControlFocusVisualPrimaryBrush"] = accentGradient,
            ["SystemControlFocusVisualSecondaryBrush"] = Brushes.Transparent,

            // Menu highlight - hover and keyboard both map to :selected on every menu surface
            // (top bar, context menus, submenus). Subtle tint, per the reference ("은은한").
            ["MenuFlyoutItemBackgroundPointerOver"] = accentMenuTint,
            ["MenuFlyoutItemBackgroundPressed"] = accentMenuTintPressed,

            // Subtitle grid selected rows - the accent users stare at all day. Styles.axaml's
            // opacity keys (0.6 focused / 0.2 unfocused) keep dimming the gradient uniformly.
            ["DataGridRowSelectedBackgroundBrush"] = accentFill,
            ["DataGridRowSelectedHoveredBackgroundBrush"] = accentFill,
            ["DataGridRowSelectedUnfocusedBackgroundBrush"] = accentFill,
            ["DataGridRowSelectedHoveredUnfocusedBackgroundBrush"] = accentFill,

            // ListBox selection - Styles.axaml consumes these same keys via DynamicResource.
            ["SystemControlHighlightListAccentLowBrush"] = accentFillLow,
            ["SystemControlHighlightListAccentMediumBrush"] = accentFillMedium,
            ["SystemControlHighlightListAccentHighBrush"] = accentFill,
            ["ListBoxItemSelectedUnfocusedBackgroundBrush"] = accentFillFaint,

            // ComboBox dropdown items match the ListBox look. All three states are parse-time
            // aliases that must be overridden by their own names - Pressed included, or pressing
            // the selected item flashes the raw OS accent (probe-verified).
            ["ComboBoxItemBackgroundSelected"] = accentFillLow,
            ["ComboBoxItemBackgroundSelectedPointerOver"] = accentFillMedium,
            ["ComboBoxItemBackgroundSelectedPressed"] = accentFill,

            // TreeView (Multiple Replace groups, ASSA draw): selected keys are parse-time
            // aliases to the raw OS accent - override by name or selection stays OS-colored.
            ["TreeViewItemBackgroundSelected"] = accentFillLow,
            ["TreeViewItemBackgroundSelectedPointerOver"] = accentFillMedium,
            ["TreeViewItemBackgroundSelectedPressed"] = accentFill,

            // Selected-tab pipe: short horizontal strip, takes the full gradient cleanly.
            ["TabItemHeaderSelectedPipeFill"] = accentGradient,

            // Remaining OS-accent active states in the primary editing view.
            ["ToggleButtonBackgroundChecked"] = toggleCheckedBrush,
            ["ToggleButtonBackgroundCheckedPointerOver"] = toggleCheckedHoverBrush,
            ["ToggleButtonBackgroundCheckedPressed"] = toggleCheckedPressedBrush,
            ["TextControlSelectionHighlightColor"] = selectionHighlightBrush,
        };
        Application.Current.Resources.MergedDictionaries.Add(_resources);

        _styles = new Styles
        {
            // TextBox: Surface layer with a near-invisible border.
            new Style(x => x.OfType<TextBox>())
            {
                Setters =
                {
                    new Setter(TextBox.BackgroundProperty, surfaceBrush),
                    new Setter(TextBox.ForegroundProperty, foregroundBrush),
                    new Setter(TextBox.BorderBrushProperty, borderStrongBrush),
                }
            },
            new Style(x => x.OfType<TextBox>().Class(":focus").Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, surfaceBrush)
                }
            },
            new Style(x =>
                x.OfType<TextBox>().Class(":pointerover").Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, surfaceHoverBrush)
                }
            },

            // Same guard as ApplyLighterDark: the two PART_BorderElement styles above paint an
            // opaque fill that would wipe out the border of an editable ComboBox (its inner
            // text box covers the whole text column). Undo them inside ComboBox templates -
            // later in the collection wins.
            new Style(x => x.OfType<ComboBox>().Template().OfType<TextBox>()
                .Class(":focus").Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, Brushes.Transparent)
                }
            },
            new Style(x => x.OfType<ComboBox>().Template().OfType<TextBox>()
                .Class(":pointerover").Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, Brushes.Transparent)
                }
            },

            // Foreground-only overrides, mirroring ApplyLighterDark's coverage.
            new Style(x => x.OfType<Button>())
            {
                Setters =
                {
                    new Setter(Button.ForegroundProperty, foregroundBrush)
                }
            },
            new Style(x => x.OfType<NumericUpDown>())
            {
                Setters =
                {
                    new Setter(NumericUpDown.BackgroundProperty, surfaceBrush),
                    new Setter(NumericUpDown.ForegroundProperty, foregroundBrush),
                    new Setter(NumericUpDown.BorderBrushProperty, borderStrongBrush),
                }
            },
            new Style(x => x.OfType<ComboBox>())
            {
                Setters =
                {
                    new Setter(ComboBox.ForegroundProperty, foregroundBrush),
                    new Setter(ComboBox.BackgroundProperty, surfaceBrush),
                    new Setter(ComboBox.BorderBrushProperty, borderStrongBrush),
                }
            },
            new Style(x => x.OfType<RadioButton>())
            {
                Setters =
                {
                    new Setter(RadioButton.ForegroundProperty, foregroundBrush)
                }
            },
            new Style(x => x.OfType<CheckBox>())
            {
                Setters =
                {
                    new Setter(CheckBox.ForegroundProperty, foregroundBrush),
                    // The app-wide ControlCornerRadius=8 lands on the 20px glyph box too (80%
                    // rounding - reads as a blob next to circular RadioButtons); pin it back
                    // to the ~4px the Fluent checkbox visuals were drawn for.
                    new Setter(CheckBox.CornerRadiusProperty, new CornerRadius(4)),
                }
            },
            new Style(x => x.OfType<ListBox>())
            {
                Setters =
                {
                    new Setter(ListBox.ForegroundProperty, foregroundBrush)
                }
            },
            new Style(x => x.OfType<DataGrid>())
            {
                Setters =
                {
                    new Setter(DataGrid.ForegroundProperty, foregroundBrush)
                }
            },
            new Style(x => x.OfType<Label>())
            {
                Setters =
                {
                    new Setter(Label.ForegroundProperty, foregroundBrush)
                }
            },
            new Style(x => x.OfType<TextBlock>())
            {
                Setters =
                {
                    new Setter(TextBlock.ForegroundProperty, foregroundBrush)
                }
            },

            // AvaloniaEdit TextArea, incl. caret and the focus/pointerover states that would
            // otherwise revert the foreground.
            new Style(x => x.OfType<TextArea>())
            {
                Setters =
                {
                    new Setter(TextArea.ForegroundProperty, foregroundBrush),
                    new Setter(TextArea.CaretBrushProperty, foregroundBrush),
                }
            },
            new Style(x => x.OfType<TextArea>().Class(":focus"))
            {
                Setters =
                {
                    new Setter(TextArea.ForegroundProperty, foregroundBrush),
                }
            },
            new Style(x => x.OfType<TextArea>().Class(":pointerover"))
            {
                Setters =
                {
                    new Setter(TextArea.ForegroundProperty, foregroundBrush),
                }
            },
            new Style(x => x.OfType<TextArea>().Class(":focus").Class(":pointerover"))
            {
                Setters =
                {
                    new Setter(TextArea.ForegroundProperty, foregroundBrush),
                }
            },

            new Style(x => x.OfType<MenuItem>())
            {
                Setters =
                {
                    new Setter(MenuItem.ForegroundProperty, foregroundBrush),
                    // PART_LayoutRoot template-binds CornerRadius - rounds the accent-tint
                    // highlight into a soft chip per the reference.
                    new Setter(MenuItem.CornerRadiusProperty, new CornerRadius(6)),
                }
            },

            new Style(x => x.OfType<Optris.Icons.Avalonia.Icon>())
            {
                Setters =
                {
                    new Setter(Optris.Icons.Avalonia.Icon.ForegroundProperty, foregroundBrush)
                }
            },

            // Floating layers: Elevated surface + hairline edge.
            new Style(x => x.OfType<ContextMenu>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, elevatedBrush),
                    new Setter(TemplatedControl.ForegroundProperty, foregroundBrush),
                    new Setter(TemplatedControl.BorderBrushProperty, borderSubtleBrush),
                }
            },
            new Style(x => x.OfType<FlyoutPresenter>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, elevatedBrush),
                    new Setter(TemplatedControl.ForegroundProperty, foregroundBrush),
                    new Setter(TemplatedControl.BorderBrushProperty, borderSubtleBrush),
                }
            },

            // Grid headers: brightest opaque layer, no hard divider lines.
            new Style(x => x.OfType<DataGridColumnHeader>())
            {
                Setters =
                {
                    new Setter(DataGridColumnHeader.BackgroundProperty, headerBrush),
                    new Setter(DataGridColumnHeader.ForegroundProperty, foregroundBrush),
                }
            },

            new Style(x => x.OfType<ButtonSpinner>())
            {
                Setters =
                {
                    new Setter(ButtonSpinner.BackgroundProperty, surfaceBrush),
                    new Setter(ButtonSpinner.ForegroundProperty, foregroundBrush),
                    new Setter(ButtonSpinner.BorderBrushProperty, borderStrongBrush),
                }
            },

            // If a platform ever rejects window transparency, popups degrade to the opaque
            // Elevated look instead of Fluent's white PART_TransparencyFallback behind the
            // translucent fill (which would wash menus out).
            new Style(x => x.OfType<PopupRoot>())
            {
                Setters =
                {
                    new Setter(TopLevel.TransparencyBackgroundFallbackProperty, elevatedOpaqueBrush),
                }
            },
        };

        // Typography only while the app font setting is unset ("Default") - an explicit user
        // font choice wins. Ordering makes this safe: both startup (Program.ConfigureApplication)
        // and settings-save (MainViewModel.ApplySettings) run UiUtil.SetFontName BEFORE
        // UiTheme.SetCurrentTheme, so these later-added styles override SetFontName's on the
        // same control types, and Remove() drops them on theme switch.
        if (IsDefaultFontSentinel(Se.Settings.Appearance.FontName))
        {
            AddFontStyles(_styles);
        }

        Application.Current.Styles.Add(_styles);
    }

    /// <summary>Same control coverage as <c>UiUtil.SetFontName</c>.</summary>
    private static void AddFontStyles(Styles styles)
    {
        var font = new FontFamily(UiFontChain);

        styles.Add(new Style(x => x.OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.FontFamilyProperty, font) }
        });
        styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters = { new Setter(TextBox.FontFamilyProperty, font) }
        });
        styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters = { new Setter(Button.FontFamilyProperty, font) }
        });
        styles.Add(new Style(x => x.OfType<MenuItem>())
        {
            Setters = { new Setter(MenuItem.FontFamilyProperty, font) }
        });
        styles.Add(new Style(x => x.OfType<Label>())
        {
            Setters = { new Setter(Label.FontFamilyProperty, font) }
        });
        styles.Add(new Style(x => x.OfType<ComboBox>())
        {
            Setters = { new Setter(ComboBox.FontFamilyProperty, font) }
        });
    }

    internal static void Remove()
    {
        FocusedButtonBrush = null;

        if (Application.Current == null)
        {
            return;
        }

        if (_resources != null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(_resources);
            _resources = null;
        }

        if (_styles != null)
        {
            Application.Current.Styles.Remove(_styles);
            _styles = null;
        }
    }
}
