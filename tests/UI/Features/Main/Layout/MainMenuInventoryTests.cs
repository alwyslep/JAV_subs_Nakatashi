using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Nikse.SubtitleEdit;
using Nikse.SubtitleEdit.Features.Main;
using Nikse.SubtitleEdit.Features.Main.Layout;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace UITests.Features.Main.Layout;

/// <summary>
/// Fork safety net for the UI/UX rework.
///
/// The fork intends to re-group, rename and move main-menu entries. The one thing that must never
/// happen is a command quietly falling out of the menu — "every feature in the original is
/// preserved" has to be enforced mechanically, not promised. These tests pin the set of commands
/// reachable from the menu against a checked-in baseline.
///
/// Two distinct failure modes, deliberately split so the message tells you which one you hit:
///   * a baseline command disappeared  -> we broke something; fix the menu, never the baseline.
///   * a command appeared              -> usually upstream added a feature; place it in our layout
///                                        and then refresh the baseline.
/// The second one is the tripwire that makes upstream menu changes impossible to merge silently.
/// </summary>
public class MainMenuInventoryTests
{
    private const string BaselineFileName = "main-menu-inventory.baseline.txt";

    private static MainViewModel BuildViewModelWithMenu()
    {
        var services = new ServiceCollection();
        services.AddSubtitleEditServices();
        var provider = services.BuildServiceProvider();

        var vm = provider.GetRequiredService<MainViewModel>();
        InitMenu.Make(vm);
        return vm;
    }

    // The baseline lives next to this source file. Resolving it via CallerFilePath keeps it out of
    // the csproj (no upstream file touched) and makes regeneration a plain file write.
    private static string BaselinePath([CallerFilePath] string thisFile = "")
        => Path.Combine(Path.GetDirectoryName(thisFile)!, BaselineFileName);

    private static IReadOnlyList<string> ReadBaseline(string path)
        => File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    private static void WriteBaseline(string path, IReadOnlyList<string> commands)
    {
        var content = new List<string>
        {
            "# Main-menu command inventory - fork safety net.",
            "# One MainViewModel command property per line, sorted.",
            "#",
            "# A command REMOVED from this list means functionality was lost. Fix the menu.",
            "# A command ADDED means upstream (or we) introduced one - place it in our layout,",
            "# then regenerate with:  ./tools/build.ps1 menu-baseline",
            "#",
            $"# {commands.Count} commands.",
        };
        content.AddRange(commands);
        File.WriteAllLines(path, content);
    }

    [AvaloniaFact]
    public void MainMenu_ExposesCommands()
    {
        var vm = BuildViewModelWithMenu();

        var commands = MainMenuInventory.CaptureCommandNames(vm);

        // Guards the harness itself: if DI or InitMenu silently produced an empty menu, the
        // set-comparison tests below would "pass" by comparing nothing to nothing.
        Assert.True(commands.Count > 100, $"Expected a populated menu, found {commands.Count} commands.");
    }

    [AvaloniaFact]
    public void MainMenu_EveryLeafIsTraceableToACommand()
    {
        var vm = BuildViewModelWithMenu();

        var untraceable = MainMenuInventory.CaptureUntraceableLeaves(vm);

        Assert.True(
            untraceable.Count == 0,
            "These menu leaves are not backed by a MainViewModel command, so the inventory cannot "
            + "protect them from being dropped during re-grouping:"
            + Environment.NewLine + string.Join(Environment.NewLine, untraceable.Select(x => "  " + x)));
    }

    [AvaloniaFact]
    public void MainMenu_RetainsEveryBaselineCommand()
    {
        var vm = BuildViewModelWithMenu();
        var commands = MainMenuInventory.CaptureCommandNames(vm);
        var path = BaselinePath();

        if (!File.Exists(path))
        {
            WriteBaseline(path, commands);
            Assert.Fail(
                $"No baseline existed, so one was written with {commands.Count} commands: {path}"
                + Environment.NewLine + "Review it, commit it, and re-run.");
        }

        var baseline = ReadBaseline(path);
        var missing = baseline.Except(commands, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} command(s) present in the baseline are no longer reachable from the menu. "
            + "Functionality was lost - fix the menu rather than the baseline:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing.Select(x => "  - " + x)));
    }

    [AvaloniaFact]
    public void MainMenu_HasNoCommandsMissingFromBaseline()
    {
        var vm = BuildViewModelWithMenu();
        var commands = MainMenuInventory.CaptureCommandNames(vm);
        var path = BaselinePath();

        if (!File.Exists(path))
        {
            return; // Covered by MainMenu_RetainsEveryBaselineCommand, which writes the baseline.
        }

        var baseline = ReadBaseline(path);
        var added = commands.Except(baseline, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(
            added.Count == 0,
            $"{added.Count} command(s) are in the menu but not in the baseline - most likely an upstream "
            + "addition that still needs a home in the fork's layout. Place it, then run "
            + "./tools/build.ps1 menu-baseline:"
            + Environment.NewLine + string.Join(Environment.NewLine, added.Select(x => "  + " + x)));
    }
}
