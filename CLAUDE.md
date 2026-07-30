# JAV_subs_Nakatashi

Fork of [SubtitleEdit/subtitleedit](https://github.com/SubtitleEdit/subtitleedit) (MIT),
currently rebased onto upstream `93f828633` (**v5.1.0-rc18**). Only the **repository** was
renamed — the solution, assemblies, and namespaces are deliberately unchanged (see
*Fork policy*).

- `origin`   → `https://github.com/alwyslep/JAV_subs_Nakatashi.git`
- `upstream` → `https://github.com/SubtitleEdit/subtitleedit.git`

## Where we are (2026-07-30)

The fork has **two** bodies of work. This section is the first — the UI/UX rework, complete. The
second is *JAV metadata + the shared catalogues*, its own section further down.

### UI/UX rework (complete)

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
- ~~**Accepted scope:** ~20 tool-window sites still pin local `"Default"`…~~ **Closed in Phase 6
  (2026-07-28)** — see below. Pretendard JP ships 400/700 only, so SemiBold kanji closest-matches
  to 700 (Korean UI headers rarely contain kanji).
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

**Done:** Phase 4 (2026-07-27) — menu re-grouping, the last roadmap item. All of it is in
`InitMenu.cs`; the safety net proved the point (baseline diff was **+2 / −0**).

What moved and why:
- **Tools 25 → 18**, alphabetical build-time sort dropped for four hand-ordered blocks
  (find-and-fix / merge lines / split+order / whole-file). The sort was splitting obvious
  siblings ("Adjust durations" from "Apply duration limits", via "AI review") and re-ordered
  the whole menu in every other UI language, so no muscle memory was possible. Same treatment
  for **ASSA tools** (Styles/Properties/Attachments first — the sort had buried Styles last).
- **Synchronization 6 → 14**: the six timing commands upstream filed under Tools, plus
  Set video offset and SMPTE timing promoted out of `Video > More`.
- **`Video > More` dissolved.** It mixed transcoding, a view toggle and two timing commands
  under a header that said nothing, at depth 3 — where `DisplayShortcuts` never reached.
- **Spell check ← Word lists** (it is the data spell check reads), **Translate ← Make new
  empty translation**, **Help last** (ASSA/SSA were appended after it).
- **Options gained Layout + Source view**, which had *no* menu home upstream: they existed
  only as toolbar buttons, so switching the button off made the feature unreachable. Headers
  reuse `Se.Language.Options.Shortcuts.GeneralChooseLayout` / `.SourceView`, already
  translated everywhere, so **no language file was touched**. This is the whole 143 → 145.
- **File > Export**: the two text-format exports moved above the 18 image/broadcast formats.

Fixes made while in there:
- `DisplayShortcuts` only walked two levels, so every depth-3 item (all of Import/Export,
  all of `Video > More`) silently showed no accelerator even with a shortcut bound. It now
  recurses (`StampShortcuts`).
- `menu.Opened` and `menu.Styles` accumulated one copy per language switch. Both are now
  detached/cleared first, and the handler is a single stateless static that reads the Menu
  and view model off the event sender, so it stays correct with several windows open.

Decisions — do not re-litigate:
- **The macOS mirror (`InitNativeMacMenu.cs`) was deliberately NOT updated.** It is a
  hand-written parallel structure with no test coverage that had already drifted from
  `InitMenu` before this change (`ExportImscImageCommand` and the whole SSA menu are missing
  there). This fork is Windows-only; mac keeps upstream's arrangement. Deriving it from
  `InitMenu` would be a real fix, but it is its own project.
- **Every video-dependent command kept its gate.** Dissolving `More` meant re-applying by
  hand the `IsVideoLoaded` binding that container used to provide: Cut video, Re-encode
  video, Set video offset and SMPTE timing carry it directly (`ToggleSmpteTiming` returns
  immediately with no message when there is no video, so an enabled entry would be a silent
  no-op), and the secondary-subtitle pair keeps *both* conditions through a `MultiBinding`
  with `BooleanAndConverter` rather than dropping one. The separators introducing those
  blocks are gated too, or the menu ends on a rule with nothing under it — which is the
  app's startup state. **Toggle waveform toolbar is the one deliberate un-gating**: it only
  flips a bool the waveform strip binds, and that strip exists without a video.
- **Two label traps, both hit here.** The waveform entry now uses `Main.Menu.WaveformToolbar`
  (what the macOS mirror already used) instead of upstream's `Options.Shortcuts.*` string,
  which is empty in some translations. Layout and Source view have no menu-side key at all,
  so they borrow the Shortcuts labels through `OrFallback` — an empty JSON value overrides
  the English C# default, which would otherwise render blank rows. Those two consequently
  have no access key, and their ellipsis is appended in code.
- **Access keys can collide when items move.** "Adjust durations" carries `_A`, which
  "Adjust all times" already owned in Synchronization; a duplicate mnemonic stops the key
  from invoking at all (it only moves focus). The moved item's underscore is stripped at the
  call site. Check for this whenever an item changes menus.

Traps for anyone touching this menu again:
- **Two commands are invisible to the safety net**: `CommandFileClearRecentFilesCommand`
  (File > Reopen) and `RunPluginCommand` (Plugins) only exist inside runtime-filled
  containers, which are empty in the headless test. Delete either and all four tests stay
  green. Guard them by hand.
- **Three items are visible/hidden PAIRS sharing one command** (`RightToLeftToggleCommand`
  in Edit, `ToggleCurrentSubtitleWhilePlayingCommand` in Video). Dropping exactly one half
  also keeps the tests green while leaving a permanently-stuck entry — move pairs together.
- `vm.MenuReopen`, `vm.MenuPlugins` and `vm.AudioTraksMenuItem` must stay the *same
  instances* published on the view model; their runtime fillers find them by reference and
  will silently mutate an orphan otherwise.
- Regenerate the baseline (`./tools/build.ps1 menu-baseline`) only on a plugin-free machine —
  installed plugins add `RunPluginCommand` to the inventory. The task deletes the file first,
  so always review `git diff` on it: **a removed line means lost functionality.**

~~Known drift this created: `docs/` still describes some old menu paths.~~ **Fixed in Phase 6**
(2026-07-28) — 14 paths across 9 files, each checked against `InitMenu.cs` rather than inferred.

**Done:** Phase 5 (2026-07-27) — subtitle grid legibility. Three small, independent changes:

- **`Show`/`Hide` → 시작/종료 in Korean.** Upstream's column names are a literal translation of
  SubRip-era "Show/Hide"; the bindings are `StartTime`/`EndTime`. Renamed at the shared resource
  (`Korean.json` `general.show`/`hide`, plus `endTime`, `showStartColumn`/`showHideColumn` and
  SortBy's two labels), so all ~30 windows with a start/end column follow. **Korean only** — English
  and the other 38 translations still say their own "Show"/"Hide"; extend deliberately, per language.
  `CompareWindow.cs` is the **one** site out of 54 where `General.Show` means "display" rather than
  a start time (it labels the All / only-differences filter); it now reads `General.Visible`, whose
  Korean value is already "표시" — so that label is byte-identical to before in Korean and no new
  resource key was needed. (In English it changes "Show:" → "Visible:", the one non-Korean-visible
  effect of this phase.) Anything added later that reuses `General.Show` must mean *start time*.
- **Tabular figures (`tnum`)** on the grid's start/end/duration cells and on `TimeCodeUpDown` /
  `SecondsUpDown` (the latter reach ~20 windows). Equal-width digits turn the `:` and `,`
  separators into vertical rules. Chosen over a monospace family: no extra embedded font, and the
  Inter/Pretendard chain from Phase 2 stays intact. Fonts without the feature ignore it, so this is
  safe on every theme. The duration column is additionally **right**-aligned — it is the one time
  column whose text length varies. `MeasureShowHideColumnWidth()`'s "widest digit" hack is now
  redundant for the start/end columns but stays correct (and still applies to non-`tnum` fonts).
- **12px left inset on the Text and Original columns** (`TextColumnLeftInset`), added after seeing
  the right-aligned duration next to the left-aligned text: the duration cell's warning background
  fills its cell, so that coloured slab stopped 12px short of the first character and number and
  sentence read as one run. Now 24px. Done as padding, not the spacer column that the
  `ScrollbarGutter` precedent invites — a spacer adds a second separator to the header row and
  needs its own exclusion in both `AutoFitColumns` and the column-width persistence. The header
  label follows the inset through **`HeaderTemplate`**, deliberately not a `TextBlock` header:
  `AutoFitColumns` finds the two stretchy columns by `column.Header.ToString()`, so a control there
  would silently stop them star-sizing. (`DataGridColumn` has `CellTheme` and `HeaderTemplate` but
  no `HeaderTheme`, so per-column header padding has to go through the template.) Original gets the
  same inset because in translation mode the two sit side by side.
- **Auto-select while playing defaults ON**, with `SubtitleGridCenterSelectedRow`. The feature was
  already fully built (`SelectCurrentSubtitleAtPlayhead` → `SelectAndScrollToRow`, waveform-toolbar
  toggle, menu entry) — upstream just ships it off, so the waveform tracked playback while the grid
  sat frozen. Centering is what makes it usable: without it the selection walks to the bottom edge
  and the grid jumps a page at a time.

**Trap, cost an entire verification cycle:** the language JSONs are **`AvaloniaResource`s**
(`UI.csproj`: `Assets\Languages\*.json`), not content files. They are unpacked to
`<DataFolder>\Languages\` by `LanguageInitializer`, which only runs when `Languages\version.txt`
is **missing or older than `Se.Version`** (`LanguageInitializer.cs:51-76`). Editing a translation
therefore changes **nothing** at runtime until the version bumps. During development, delete
`<output>\Languages\version.txt` and restart. Worse, the unpack races the first `LoadLanguage()`:
the run that rewrites the files still displays the **old** strings, so it takes **two** restarts to
see a translation edit. Real releases are fine (`Se.Version` bumps), but a settings-folder that
survives a downgrade will keep newer strings.

Also note `SeGeneral`'s defaults only apply to a **fresh** settings file — an existing
`Settings.json` keeps whatever it stores, so a default flip is invisible to current users.

**Done:** Phase 6 (2026-07-28) — the loose ends the earlier phases deliberately parked.

- **Font chain reaches the tool windows.** Phase 2's parked ~20 sites are done: 24 sites take
  the one-line `&& !NakatashiTheme.OwnsSubtitleFont(...)` guard Phase 2 established (a script
  applied them; the exact upstream one-liner was byte-identical everywhere). Three needed a
  different shape — `SpellCheckViewModel` / `PromptUnknownWordViewModel` hold the name in a local
  reused across 4 and 6 pins, so the local is emptied **once** (`string.Empty`, not `null` — the
  property is non-nullable and the tree must stay at 0 warnings); `OcrViewModel` assigns
  `TextBoxFontFamily` *unconditionally*, so skipping is not enough and the new
  `NakatashiTheme.SubtitleSurfaceFont` hands it the chain instead.
  **AvaloniaEdit `TextEditor` is now a deliberate exclusion, not a leftover** — it pins no font at
  all and inherits AvaloniaEdit's monospace default, which is *correct* for source view (line
  numbers, timecode columns) and media info (key/value dump). A proportional face makes both worse.
- **`tnum` on the last four numeric columns** (gap, CPS, WPM, pixel width). The number column is
  left alone on purpose: it is centre-aligned in a StackPanel beside the bookmark icon and built
  through a fluent `UiUtil.MakeLabel().WithBindText()` expression — small gain, large diff.
- **`docs/` menu paths fixed** (14 spots / 9 files), each verified against `InitMenu.cs`.
  Also caught: `faq.md` ×2 and `audio-visualizer.md` pointed at `Video > More`, which Phase 4
  dissolved entirely.
- **Translation prompt editor got a reset button.** *Correction to the fork's own docs:* the
  editor was NOT missing — `TranslateSettingsWindow` (opened by `AutoTranslateViewModel.OpenSettings`)
  has had per-engine prompt load/save for 13 engines plus `{0}`/`{1}` validation all along. The
  earlier note in `docs-fork/ai-register-proposal.ko.md` claimed otherwise because it grepped only
  `AutoTranslateWindow.cs`; both docs are corrected. What was genuinely missing is recovery:
  `LoadValues` falls back to the shipped default only when the stored prompt is **blank**, so any
  non-empty edit was sticky forever. The label reuses `Se.Language.Tools.AiReview.ResetToDefault`
  (present and non-empty in **all 32** language files) so **no language file was touched**.

**Trap worth keeping:** the script that applied the 24 guards wrote with Python's `utf-8-sig`,
which adds a BOM unconditionally — it silently added one to 12 files that had none, dirtying the
first line of each. Caught by diffing the working tree against `git show HEAD:<file>` byte-wise.
Any bulk rewrite of upstream files must **preserve the original BOM state and line endings per
file** (this repo is LF-only, no CRLF).

Roadmap: ~~1 palette/surfaces~~ → ~~2 typography~~ → ~~3 accent gradient + translucent
overlays~~ → ~~4 menu re-grouping~~ → ~~5 grid legibility~~ → ~~6 parked loose ends~~.
**The UI/UX rework is complete.**

### JAV metadata + the shared catalogues (branch `feat/jav-metadata`, 2026-07-29 → 30)

The second body of fork work, and a different kind: not a re-skin but **making the editor read and
write the same data the sibling translator already has.** The full record — every measurement, every
reversed judgement — is `docs-fork/video-metadata-plan.ko.md`; this is the map.

The premise, measured across five drives: 22,296 mp4 / 0 mkv, **no sidecar metadata at all**, so
the mp4 atoms are the only in-file store — and they carry the thing `CLAUDE.md` said was destroyed
the moment a Korean SRT exists: **relationship information, in Japanese** (`©cmt` synopsis 57%,
`©gen` genres 88%, `©ART` cast 94%). Better still, the translator had already produced per-film
speech-level guidebooks and a 27,136-row glossary. So the editor **reads the same SQLite files**
rather than re-deriving anything.

| Stage | What landed |
|---|---|
| 1 | `Logic/Media/VideoTagInfo.cs` — TagLib# MP4 reader (already a dependency; no ffprobe), surfaced in the media-info window |
| 2 | `Logic/JavData/` — `JavDataPaths` / `JavDb` / `JavGuidebook` / `JavCatalog`, `Microsoft.Data.Sqlite`, `SeJavData` paths (`D:\` default, overridable both sides) |
| 3 | `Logic/JavData/SpeakerContext.cs` — the speech-level relationship note fills itself from guidebook → tags → catalogue, **0 extra LLM calls** |
| 4 | Writing back: guidebook (`pinned`) and glossary (`JavTerms.Pin`); the translator's `terms` gained a `pinned` column so a machine cannot overwrite a person's choice |
| 5 | Deterministic name back-check — **built, measured, and abandoned**; 5′ injected settled spellings into AI review instead |
| 6 | `Features/Tools/NameCheck/` — the LLM name-consistency pass; menu inventory **145 → 146** |
| 7 | `Features/Tools/NameCheck/OriginalDialogue.cs` — reads the film's own original-language subtitle so a name fix can be pinned at all, and gates pinning on what it says |

Hard-won facts — do not re-derive:

- **The release code comes from a catalogue lookup, not a regex.** `©alb` is the code only 57% of
  the time (it is a series name otherwise, and on at least one file it is *another film's* code),
  and codes come in ≥6 shapes. Matching the file-name stem against `catalog` (95,871 rows) resolves
  **97%** of 400 sampled files and returns the catalogue's own spelling, which is what makes the
  editor's key agree with the translator's.
- **Accumulation unit differs per layer** (the translator's `regdb.py` header nails this down):
  spellings are global/series facts, **relationships are per-film facts** — carrying a speech level
  across films makes it false, not stale.
- **A name is transliterated, never translated.** 18% of the tags are Korean MT and carry
  `鈴の家りん → 린의 집 인`. `SpeakerContext.TrustedNames` drops **the whole cast list** if any one
  entry contains Hangul: the surviving Latin fragments only survived by not being Korean, and a
  wrong cast is worse than none (the model hunts for those people in the dialogue and reasons from
  failing to find them).
- **Latin-only `src` in the glossary is not pollution.** Measured: 11.9% of `quality='ok'` rows
  (2,154), incl. 93 `%-san` / 53 `%-kun` / 187 `%-chan` — harvested from the library's 3,819 English
  subtitles. `JavTerms.CanPin` rejecting Hangul but allowing romanisation is **parity with the
  translator**, not a hole. Narrowing it would give the editor its own private rule.
- **Name-check pin rate is a metadata-coverage function, not a model-quality one.** Measured over 8
  real subtitles against a hosted model: 5 files found nothing, 4 findings survived the guards, 2
  were pinnable. The two that were pinnable had the original form because a *video tag* supplied it.
- **The language code in a subtitle's file name is a lie about a third of the time.** Measured over
  400 files named `*.ja.srt`: 257 Japanese, **124 Chinese**, 16 romanised, 3 Korean —
  `JUL-224.zh.ja.srt` and `JUR-268-zh-tw-繁中.ja.srt` are real names. Anything that reads "the
  original language" off a file name will read Chinese a third of the time; a live run wrote 希米卡
  over the correct 由美香 exactly this way. Kana share separates them cleanly (bimodal: 0-5% for 142
  of them, 70-100% for 256, one file anywhere in between).
- **Translation preserves timecodes, so aligning two subtitles is exact or wrong — never fuzzy.**
  Median share of cues matching within 100 ms: 98%; widening the window to 2 s leaves that median at
  98% and only lets wrong lines match. A subtitle does not record which file it was translated from,
  so the match rate itself is the test of whether the right file was picked: real sources score
  61-100%, wrong cuts 1-11%.
- **Stage 7 is a correctness feature, not a productivity one, and the docs say so.** Filling in the
  original form removed the accident that had been protecting the glossary (an empty source), so the
  same call that fills it is asked to classify too. Two runs over the same 8 films gave 3 pinnable
  names then 1 — the first pass is non-deterministic, so no pin-rate claim is available. What is
  measured: on the one film where the check could run, **2 of 2 findings had the direction reversed**
  (히노코리 offered as correct over 히노보리, while the original said ひのぼり).
- The three name-check guards each came from an observed failure: un-deduplicated text hit the
  40,000-char cap at line ~3,000 of a 6,582-line file (everything after was never examined);
  replacing a string with one that contains it self-feeds (`사카미치 사카미치 미루 사카미치 미루`);
  and swapping a form of address leaves the particle on the wrong ending
  (`사카미치 미루은, 반드시`). Only `상 → 씨` is exempt — 상 is さん left half-transliterated.

## Sibling project: `srt-translator`

`C:\Users\geech\dev2\jav\subtitles\srt-translator` (repo `alwyslep/srt-translator`) is where the
Japanese→Korean translation actually happens (PySubtrans + Gemini/DeepSeek). **Speech-register
(화계) work belongs there, not here** — the relationship information lives only in the Japanese
role language and is destroyed the moment the Korean SRT exists. Subtitle Edit can only patch
after the fact. That is still true; what changed is that the editor now patches **into the same
catalogues**, so a correction made here survives into the next translation run.

A `feat/register` branch is checked out as a **git worktree** at
`…\subtitles\srt-translator-register` (`git worktree add` does not need a clean tree, so the
main worktree's in-progress edits stay untouched). Three Python-specific gotchas: venvs are
per-worktree and `.gitignore` does not cover `.venv/`; `%APPDATA%\AI-SRT-Translator\terminology\`
is **shared** between worktrees, so an A/B comparison cross-contaminates the term snapshots.
**Only the main worktree has a `.venv`** — that is the one that actually runs.

### The two branches must both carry the DB contract (2026-07-30)

The shared catalogues are now a **contract between two programs**, so a translator branch that does
not know about it does damage rather than merely lagging. Measured on `main` before the fix:
`termdb.py` had **zero** mentions of `pinned`, so `save_series()`'s re-harvest overwrote spellings
the editor had pinned; and `guidebook.py` had zero mentions of SQLite, so it could not see the 42
guidebook rows already migrated out of `%APPDATA%`. The DB migration and `ALTER` had already run, so
this was live, not theoretical.

The two commits (`paths.py` + the `pinned` column) are therefore on **both** branches — cherry-picked
to `main` as `257a65e` / `e47d788`, 0 conflicts. The A/B baseline is intact: the only difference
between the branches is still `meta.py`'s `PRESCAN_PROMPT` and `instructions/pornify.txt`. Verified
afterwards: all **17** Python self-check modules pass (`python -m srt_translator.test_*` — there is
no pytest in the venv), `paths.*()` returns the same `D:\` paths as before, and `guidebook.load()`
reads from SQLite.

**Anything that changes the DB contract goes to both branches.** A prompt experiment does not.

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
| `tests/{libse,libuilogic,seconv,UI}` | xUnit | 2,136 tests |

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

**Baseline (verified 2026-07-30, on `93f828633` / rc18 + `feat/jav-metadata`):** clean `Release`
build with **0 warnings, 0 errors**; `2134 passed / 1 failed / 1 skipped` (see below);
`publish win-x64 --self-contained` produces a 138 MB single-file `SubtitleEdit.exe`
(263 MB with libmpv and the native Skia/HarfBuzz DLLs alongside; last measured on rc17).
Fork CI reproduced the rc17 counts on `windows-latest`, and `ubuntu-latest` built clean.

### Upstream sync rc17 → rc18 (2026-07-29)

66 upstream commits, **0 conflicts**. The fork policy paid for itself: of our 67 touched
files and upstream's 107, only **6 overlapped** — and all six auto-merged (largest was
`OcrViewModel.cs`, upstream `+22/-11` against our `+7/-3`). **`InitMenu.cs`, the fork's
deepest change, upstream never touched.**

The menu inventory net stayed green, which is the load-bearing fact: 66 upstream commits
added **no** new menu command and removed none of ours. That is the one thing a human
cannot verify by looking.

What the sync buys: upstream's `perf/span-and-alloc-hunt` (per-keystroke and per-tick work
in the main window, per-word/per-line allocations in OCR and spell check, libse save and
line-break hot paths) — i.e. the "performance" item this file lists as the next phase,
already done upstream. Plus retry-with-backoff for Mistral/ChatGPT/Anthropic/Groq/OpenRouter.

What it cost: upstream introduced **2 new build warnings** (CS0419, an ambiguous `cref` in
`src/libse/Common/RegexUtils.cs` — a file the fork had never touched). Fixed here rather
than reported, per *Fork policy*. That is a deliberate, small widening of the divergence:
the "0 warnings" gate only means something if it is held, and a tree that carries warnings
trains people to stop reading them.

The pre-sync tip is tagged **`backup/pre-rc18-sync-20260729`** (`96d2d8901`) — a rebase
rewrites every hash, so this is the only way back.

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

**Two additions from the SQLite dependency (2026-07-29) — the blanket `checkout` is no longer
safe on its own:**

- When a dependency genuinely changes, the lock file legitimately changes too, and
  `git checkout -- src/ui/packages.lock.json` then **throws away the real change** along with the
  publish artifact. Correct order: `checkout` → `dotnet restore` → convert to LF.
- **NuGet rewrites `packages.lock.json` with CRLF.** This repo is LF-only, so a legitimate
  47-line change showed up as **+737 / −619** — the whole file. Convert it back to LF before
  committing or the diff is unreviewable, which is the exact thing this file warns about under
  "any bulk rewrite must preserve the original BOM state and line endings per file".
- `Microsoft.Data.Sqlite` 10.0.10 resolves `SQLitePCLRaw` **2.1.11**, which carries a
  high-severity advisory (GHSA-2m69-gcr7-jv3q) and so reports **NU1903** — the first warning in a
  tree this fork keeps at zero. `SQLitePCLRaw.bundle_e_sqlite3` is pinned to **2.1.12** in
  `UI.csproj` with a comment saying to drop the pin once the transitive resolve is clean.
- Its native `e_sqlite3.dll` (1.89 MB) lands **beside** the single-file exe, not embedded — same
  as libmpv/Skia/HarfBuzz, so deployment is unchanged. exe 138 → **145 MB**, folder 263 → **279 MB**.

Large fetched binaries are cached in `third_party/` (libmpv is ~118 MB), which has its
own `.gitignore`. `publish/` is already covered by upstream's root `.gitignore`.

## Known-failing test

`SeConvTests.Core.VobSubPaletteTest.LoadVobSub_AppliesIdxPalette_ToDecodedBitmaps` fails
at the fork point:

> decoded bitmap has no pixel in the .idx palette's pattern colour — the CLUT was not applied

This is **pre-existing upstream**, not caused by the fork — it reproduces on an unmodified
tree, survived the rc16 → rc17 → rc18 bumps unchanged, and also fails under
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
against `main-menu-inventory.baseline.txt` (**146 commands** since the name-check tool; 145 after
Phase 4, 143 at Phase 0).

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
