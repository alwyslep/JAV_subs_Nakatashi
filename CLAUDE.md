# JAV_subs_Nakatashi

Fork of [SubtitleEdit/subtitleedit](https://github.com/SubtitleEdit/subtitleedit) (MIT),
currently rebased onto upstream `e5cc40b3e` (**v5.1.0-rc17**). Only the **repository** was
renamed — the solution, assemblies, and namespaces are deliberately unchanged (see
*Fork policy*).

- `origin`   → `https://github.com/alwyslep/JAV_subs_Nakatashi.git`
- `upstream` → `https://github.com/SubtitleEdit/subtitleedit.git`

## Where we are (2026-07-27)

Goal: re-skin the UI in a modern "Deep Gray" dark design system (the reference doc is
`C:\Users\geech\Documents\배경색 및 다크 모드 (The Deep Gray Palette).md` — Gemini / Claude-desktop
style: dense minimalism, layered surfaces, near-invisible borders, generous radii). Frame first
(*와꾸*), performance and features after.

**Hard constraint:** every feature that exists upstream must survive. Menu position and grouping
may change freely; the atomic commands may not disappear. This is enforced by the inventory tests
described below, not by promise.

**Decisions already made — do not re-litigate:**

| Question | Decision |
|---|---|
| Palette | Ship **both** variants — Claude charcoal (`#191919`/`#222222`) and Gemini cool blue (`#131314`/`#1e1f20`) — as swappable colour tables |
| Theme placement | **New theme(s) added alongside** the existing Dark; never overwrite `ApplyLighterDark()` |
| First pass scope | Safety net + visual phases only; **menu re-grouping deferred** until the new look is on screen |
| Upstream issues | **Never file any.** See *Fork policy* |

**Done:** Phase 0 — the menu inventory safety net (143 commands baselined, 4 tests green).

**Done:** Phase 1 (2026-07-27) — two Deep Gray variants, **"Nakatashi Charcoal"** and
**"Nakatashi Blue"**, selectable in the settings theme dropdown. All theme logic lives in
`src/ui/Logic/Theming/Nakatashi/` (`NakatashiPalette.cs` fixed color tables,
`NakatashiTheme.cs` applier modeled on `ApplyLighterDark()` incl. its ComboBox guards, plus
Fluent resource overrides: `ControlCornerRadius` 8 / `OverlayCornerRadius` 12, layered
surfaces Base→Surface→Elevated→Header, `white/5`–`white/10` borders). The planned "three
touch points" became five upstream files, each a minimal commented diff — recon found two
sites that bypass the central predicate and had to be touched: `UiTheme.cs` (dispatch branch,
widened `IsDarkThemeEnabled()`, theme-aware `GetDarkTheme*Color()` getters), `UiUtil.cs`
(2-line delegation — this makes `Program.cs`'s RegionColor theme-aware with **zero** diff to
`Program.cs`), `PluginThemeColorsFactory.cs` (plugins would render light), `Se.cs` (libse's
exported dark flag), `SettingsViewModel.cs` (dropdown + name maps). The Nakatashi palettes are
deliberately **decoupled** from the Dark theme's user-configurable `DarkMode*` settings.

**Resolved (Phase 1 spike):** backdrop blur is **not possible in-app** on Avalonia 12.1 —
`TransparencyLevelHint`/`ExperimentalAcrylicBorder` only blur the desktop behind the window,
and `Visual.Effect` blurs an element's own subtree. Later phases use translucent Elevated
surfaces; for modals over frozen content a `RenderTargetBitmap` snapshot + `BlurEffect` is a
viable equivalent. Do not re-spike.

**Done:** Phase 2 (2026-07-27) — typography. While a Nakatashi theme is active AND the app
font setting is unset, six app-level styles (mirroring `UiUtil.SetFontName`'s coverage) apply
`NakatashiTheme.UiFontChain`: **Inter** (from the `Avalonia.Fonts.Inter` package — resolves via
`avares://` without `WithInterFont()`, probe-verified) → **Pretendard** (embedded, weights
400/500/600/700) → **Pretendard JP** (embedded, 400/700 — base Pretendard has kana but NO CJK
ideographs; without the JP sibling every Japanese subtitle line mixed Pretendard kana with
OS-fallback kanji) → **$Default** (keeps the OS chain for symbols/other scripts). Assets in
`src/ui/Assets/Fonts/` (~13.7 MB with the OFL license embedded alongside — SIL's own FAQ says
embedded-in-binary needs no separate file, so this exceeds compliance).

Hard-won facts (adversarial review, all probe-verified — do not re-derive):
- **The unset app font is persisted as `"$Default"`** (`FontFamily.DefaultFontFamilyName`), not
  `"Default"`: upstream `SettingsViewModel.SaveSettings` writes `new Label().FontFamily.Name`.
  Any gate comparing only `== "Default"` is dead on the very settings-save that activates the
  theme. Use `NakatashiTheme.IsDefaultFontSentinel`.
- Subtitle grid/edit box set a **local** `FontFamily` from `SubtitleTextBoxAndGridFontName`
  (local beats styles). The three InitListViewAndEditBox pins skip only under
  `NakatashiTheme.OwnsSubtitleFont` (theme active + both font settings unset) — an explicit
  app font must NOT leak into subtitle surfaces on stock themes (upstream parity).
- Ordering guarantee: `SetFontName` runs BEFORE `SetCurrentTheme` at startup
  (`Program.ConfigureApplication`) and on settings save (`MainViewModel.ApplySettings`), so the
  theme's later-added font styles always win.
- **Accepted scope (decision, not oversight):** ~20 tool-window sites (SpellCheck, FixCommonErrors,
  OCR family, ConvertActors, ChangeFormatting…) still pin local `"Default"` and render the OS
  font under Nakatashi, as does AvaloniaEdit `TextEditor` (Source view / media info). Byte-identical
  to upstream; extend coverage only as a deliberate later step. Pretendard JP ships 400/700 only,
  so SemiBold kanji closest-matches to 700 (Korean UI headers rarely contain kanji).
- macOS never activates the chain (FontName defaults to "Helvetica Neue" — an explicit choice
  that also works around the caret bug).

**Done:** Phase 3 (2026-07-27) — accent gradient + translucent overlays. The Gemini-glow
gradient (`#4A77FF → #A374FF → #FF8A75`, palette slots AccentStart/Mid/End) paints ACTIVE
states only, via `DynamicResource` brush-key overrides in `NakatashiTheme.Apply` (recon
verified every key against the extracted Fluent/DataGrid 12.1 axaml): TextBox/ComboBox focus
borders, the app-wide keyboard-focus adorner (2px ring, secondary transparent), menu
highlight (subtle 0x30/0x48 tints + 6px chip), DataGrid/ListBox/ComboBox/TreeView selection,
the selected-tab pipe, ToggleButton `:checked` (solid AccentStart — gradient on a 24px pill
reads as mush), TextBox selection highlight (translucent solid AccentMid — a relative
gradient restarts per selection rectangle), and the focused dialog button (0x46 gradient wash
via the `UiUtil.GetFocusedButtonBackgroundBrush` hook — the per-button `:focus` style
outranks app styles, so the swap must live in the getter). Floating layers (menus, submenus,
flyouts, ComboBox dropdowns) are translucent Elevated at 0xE0 with a `PopupRoot`
`TransparencyBackgroundFallback` hardening; popups composite per-pixel alpha by default
(decompile-verified: Fluent PopupRoot requests Transparent; the rounded menu corners already
rely on it).

Decisions + hard-won facts (adversarial review, probe-verified — do not re-derive):
- **Parse-time alias trap**: FluentControlResources aliases are `<StaticResource>` copies —
  overriding an underlying SystemControl* key never propagates through an alias. Every alias
  must be overridden by its own name (`ComboBoxBackgroundUnfocused`, all three
  `ComboBoxItemBackgroundSelected*` incl. Pressed, all three `TreeViewItemBackgroundSelected*`).
- **AA contrast split**: fill surfaces under primary text use the darker warm end
  `AccentEndFill #D9705C` (full coral at the stock 0.6 selection opacity = 4.2:1 vs
  TextPrimary — below AA; the fill stop lands ~5.2:1). Rings/borders keep full `#FF8A75`
  (7.66:1 vs Base). Banked numbers: menu tint 0x30 over the 0xE0 layer = ΔE 15-20 vs resting,
  text 8.8-10:1; 0xE0 popup over pure white keeps 8.26:1; 0x46 button wash quieter than
  upstream's 0x63 blue yet more readable.
- **ToolTip stays opaque** (sits directly over the exact text it explains) — do not "fix" it
  to match the translucent menus. **No BoxShadow on popups**: a shadow needs a margin gutter
  that shifts menus ~8px off their anchor; deferred, not forgotten.
- **Accepted scope**: CheckBox/RadioButton/ToggleSwitch glyph fills and Slider/ProgressBar
  value tracks stay stock accent (neutral micro-controls, gradient would be noise). Modeless
  Find/Replace/AdjustAllTimes windows keep the previous theme's focused-button brush across a
  mid-session theme switch (widens an existing upstream staleness class; self-heals on
  reopen). Plugins get `AccentColor` = AccentMid hex while Nakatashi is active (a single hex
  cannot express a gradient).
- Phase 1's "UiUtil.cs 2-line delegation" note is superseded: UiUtil.cs now also carries the
  commented FocusedButtonBrush hook.

Roadmap: ~~1 palette/surfaces~~ → ~~2 typography~~ → ~~3 accent gradient + translucent
overlays~~ → 4 menu re-grouping.

`.claude/settings.json` holds a permission allowlist for the usual dotnet/git commands. Upstream's
`.gitignore` excludes `.claude/`, so it is **local-only and not in the repo** — recreate it by hand
on a fresh clone.

## Toolchain

| Requirement | Needed | Verified on this machine |
|---|---|---|
| .NET SDK | `10.0.x` | 10.0.301 |
| Windows Desktop runtime | 10.0.x | 10.0.9 |
| libmpv | runtime only, for video playback | fetched by `tools/build.ps1 mpv` |
| ffmpeg | runtime only, for waveform/audio | install separately |

Everything targets `net10.0` (plus `netstandard2.1` for `LibSE`). The UI is **Avalonia
12**, not WinForms — it builds and runs cross-platform.

`global.json` (added by this fork) requires SDK **≥ 10.0.100** and rolls forward to the
newest installed major, so a machine with only .NET 8/9 fails immediately with a clear
SDK-resolution error instead of a confusing pile of compile errors.

## Layout

| Path | Project | Output |
|---|---|---|
| `src/libse` | `LibSE` | core subtitle library (`netstandard2.1;net10.0`) |
| `src/libuilogic` | `LibUiLogic` | UI-agnostic logic |
| `src/ui` | `UI` | Avalonia desktop app → `SubtitleEdit.exe` |
| `src/seconv` | `SeConv` | `seconv` CLI converter |
| `tests/{libse,libuilogic,seconv,UI}` | xUnit | 1,938 tests |

`tests/benchmarks/UiBenchmarks.csproj` exists but is **not** in `SubtitleEdit.sln`, so
solution-wide build/test commands skip it. Build it explicitly if you need it.

## Build

`tools/build.ps1` wraps the exact commands `.github/workflows/build-ui.yml` uses, so a
local rebuild matches CI.

```powershell
./tools/build.ps1 restore
./tools/build.ps1 build    -Configuration Release
./tools/build.ps1 test
./tools/build.ps1 publish  -Runtime win-x64 -SelfContained
./tools/build.ps1 run                        # launch the UI from source
./tools/build.ps1 clean                      # drop bin/ obj/ publish/
./tools/build.ps1 all                        # restore → build → test → publish
```

Or drive `dotnet` directly:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test  --configuration Release --no-build
```

Publish output lands in `publish/<runtime>/` (gitignored). `publish` stamps the version
parsed out of `src/ui/Logic/Config/Se.cs` — that file is the single source of truth for
the app version, and both CI and `build.ps1` read it with the same regex.

**Baseline (verified 2026-07-27, on `e5cc40b3e` / rc17):** clean `Release` build with
**0 warnings, 0 errors** (~74s cold, ~4s incremental); `1937 passed / 1 failed` (see
below); `publish win-x64 --self-contained` produces a 138 MB single-file
`SubtitleEdit.exe` (263 MB with libmpv and the native Skia/HarfBuzz DLLs alongside).
Fork CI reproduced the same counts on `windows-latest`, and `ubuntu-latest` built clean.

### Gotcha: publish dirties `packages.lock.json`

`UI.csproj` sets `RestorePackagesWithLockFile`, so a **RID-specific** publish rewrites
`src/ui/packages.lock.json` — it appends a `net10.0/<rid>` section and bakes the
`-p:Version` value into the `libse` `ProjectReference` range (`[1.0.0, )` →
`[5.1.0.16, )`). That is a publish artifact, not a dependency change. Discard it:

```powershell
git checkout -- src/ui/packages.lock.json
```

`build.ps1 publish` prints a reminder when this happens. Plain `restore`/`build`/`test`
leave the lock file alone.

Large fetched binaries are cached in `third_party/` (libmpv is ~118 MB), which has its
own `.gitignore`. `publish/` is already covered by upstream's root `.gitignore`.

## Known-failing test

`SeConvTests.Core.VobSubPaletteTest.LoadVobSub_AppliesIdxPalette_ToDecodedBitmaps` fails
at the fork point:

> decoded bitmap has no pixel in the .idx palette's pattern colour — the CLUT was not applied

This is **pre-existing upstream**, not caused by the fork — it reproduces on an unmodified
tree, survived the rc16 → rc17 bump unchanged, and also fails under
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, so it is not a locale artifact. It went
unnoticed because upstream's release workflows have their test step commented out
(`.github/workflows/build-ui.yml:320`), so nothing upstream ever runs it. The test arrived
in `90646b256`; the code under test was last touched by `3edc21e9e`.

**Do not report this (or anything else) upstream.** Filing issues or PRs against
`SubtitleEdit/subtitleedit` is out of scope for this fork by the owner's decision. Fix it
here or leave it excluded.

`.github/workflows/fork-ci.yml` excludes exactly this one test so CI stays meaningful.
Remove that filter once it is fixed.

## CI

`.github/workflows/fork-ci.yml` (fork-only) runs restore + build + test on Windows and a
build on Linux for every push and PR. Upstream's workflows are all `workflow_dispatch`
release pipelines and never fire on their own — leave them alone so rebases stay clean.

## UI/UX rework — the safety net

The fork is re-skinning the UI (Deep Gray palette, layered surfaces, softer radii) and will later
re-group main-menu entries. The hard constraint is that **no feature from upstream may be lost**,
so that is enforced by test, not by promise.

`tests/UI/Features/Main/Layout/MainMenuInventoryTests.cs` resolves the real `MainViewModel` from
the DI container, builds the menu via `InitMenu.Make`, and compares the set of reachable commands
against `main-menu-inventory.baseline.txt` (**143 commands**).

Entries are keyed by **`MainViewModel` command property name — never by menu path or header text**,
because paths and wording are exactly what we intend to change, and header text moves with
upstream's translations. Menu items the VM fills at runtime (recent files, plugins, audio tracks)
are recognised by reference and exempted, so the check does not depend on the active UI language.

Four tests, each a distinct failure mode:

| Test | Fires when | What it means |
|---|---|---|
| `MainMenu_ExposesCommands` | menu is empty/tiny | the harness itself broke — the others would pass vacuously |
| `MainMenu_EveryLeafIsTraceableToACommand` | a leaf has no VM command | a blind spot: that entry could vanish unnoticed |
| `MainMenu_RetainsEveryBaselineCommand` | a baseline command is gone | **we lost functionality — fix the menu, never the baseline** |
| `MainMenu_HasNoCommandsMissingFromBaseline` | a new command appeared | usually upstream added a feature; give it a home, then refresh |

That last one is the upstream tripwire: an upstream menu addition cannot merge silently. Until it
is placed, the item still shows in its default position, so nothing is lost meanwhile.

```powershell
./tools/build.ps1 menu-baseline   # regenerate after an intentional change
```

Review the diff before committing — a **removed** line is a regression, an **added** line is new
work to place.

## Upstream sync

```powershell
./tools/sync-upstream.ps1              # rebase current branch onto upstream/main
./tools/sync-upstream.ps1 -Strategy merge
./tools/sync-upstream.ps1 -Push        # rebase then push (--force-with-lease)
```

The script refuses to run on a dirty tree.

## Fork policy

Keep divergence from upstream small and legible so `git rebase upstream/main` keeps
working:

1. **Do not rename** the solution, `AssemblyName` (`SubtitleEdit`), or `RootNamespace`
   (`Nikse.SubtitleEdit`). A rename touches nearly every file and turns every future
   rebase into a whole-tree conflict. The repository name is the only thing changed.
2. Put fork-specific files in **new** paths (`tools/`, `CLAUDE.md`, `global.json`,
   `.github/workflows/fork-ci.yml`) rather than editing upstream files.
3. When upstream files must change, keep the diff minimal and comment *why*.
4. MIT license — keep `LICENSE` and upstream attribution intact.
