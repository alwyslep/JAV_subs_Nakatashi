# JAV_subs_Nakatashi

Fork of [SubtitleEdit/subtitleedit](https://github.com/SubtitleEdit/subtitleedit) (MIT),
currently rebased onto upstream `e5cc40b3e` (**v5.1.0-rc17**). Only the **repository** was
renamed — the solution, assemblies, and namespaces are deliberately unchanged (see
*Fork policy*).

- `origin`   → `https://github.com/alwyslep/JAV_subs_Nakatashi.git`
- `upstream` → `https://github.com/SubtitleEdit/subtitleedit.git`

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
