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
        var elevatedBrush = new SolidColorBrush(p.Elevated);
        var headerBrush = new SolidColorBrush(p.Header);
        var borderSubtleBrush = new SolidColorBrush(p.BorderSubtle);
        var borderStrongBrush = new SolidColorBrush(p.BorderStrong);

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

            // Menu popups float on the Elevated layer with a hairline edge.
            ["MenuFlyoutPresenterBackground"] = elevatedBrush,
            ["MenuFlyoutPresenterBorderBrush"] = borderSubtleBrush,
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
                    new Setter(MenuItem.ForegroundProperty, foregroundBrush)
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
