using Avalonia.Controls;
using Nikse.SubtitleEdit.Features.Main;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Input;

namespace UITests.Features.Main.Layout;

/// <summary>
/// Walks the built main menu and reports which MainViewModel commands it exposes.
///
/// This exists so the fork can re-group / rename / move menu entries freely while still proving
/// mechanically that no piece of functionality fell out. Entries are keyed by the *command*
/// (the atomic unit of behaviour), never by menu path or header text, precisely because paths and
/// wording are the things we intend to change. It also stays stable across upstream's translation
/// churn, which would otherwise make a header-keyed baseline useless.
/// </summary>
public static class MainMenuInventory
{
    /// <summary>
    /// Every MainViewModel command reachable from the menu, as VM property names, sorted.
    /// </summary>
    public static IReadOnlyList<string> CaptureCommandNames(MainViewModel vm)
    {
        var namesByCommand = BuildCommandNameMap(vm);
        var found = new SortedSet<string>(System.StringComparer.Ordinal);

        foreach (var item in vm.Menu.Items)
        {
            Walk(item, namesByCommand, found);
        }

        return found.ToList();
    }

    /// <summary>
    /// Menu leaves whose command could not be traced back to a MainViewModel property. A non-empty
    /// result means the inventory has a blind spot — those entries could vanish unnoticed.
    /// </summary>
    public static IReadOnlyList<string> CaptureUntraceableLeaves(MainViewModel vm)
    {
        var namesByCommand = BuildCommandNameMap(vm);
        var runtimeManaged = BuildRuntimeManagedContainers(vm);
        var untraceable = new List<string>();

        foreach (var item in vm.Menu.Items)
        {
            CollectUntraceable(item, string.Empty, namesByCommand, runtimeManaged, parentIsRuntimeManaged: false, untraceable);
        }

        return untraceable;
    }

    /// <summary>
    /// MenuItems the view model holds a reference to are filled in at runtime (recent files, the
    /// plugin list, the video's audio tracks, ...). In a headless test with no video, no plugins and
    /// no history they are empty, or hold a "nothing here" placeholder — which looks exactly like an
    /// unbacked leaf but is not one. Identify them by reference so this does not depend on the
    /// active UI language.
    /// </summary>
    private static HashSet<MenuItem> BuildRuntimeManagedContainers(MainViewModel vm)
    {
        var containers = new HashSet<MenuItem>(ReferenceEqualityComparer.Instance);

        foreach (var property in typeof(MainViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(MenuItem) || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (property.GetValue(vm) is MenuItem menuItem)
            {
                containers.Add(menuItem);
            }
        }

        return containers;
    }

    private static void Walk(object? node, IDictionary<ICommand, string> namesByCommand, ISet<string> found)
    {
        if (node is not MenuItem menuItem)
        {
            return;
        }

        if (menuItem.Command != null && namesByCommand.TryGetValue(menuItem.Command, out var name))
        {
            found.Add(name);
        }

        foreach (var child in menuItem.Items)
        {
            Walk(child, namesByCommand, found);
        }
    }

    private static void CollectUntraceable(
        object? node,
        string path,
        IDictionary<ICommand, string> namesByCommand,
        HashSet<MenuItem> runtimeManaged,
        bool parentIsRuntimeManaged,
        ICollection<string> untraceable)
    {
        if (node is not MenuItem menuItem)
        {
            return;
        }

        var header = menuItem.Header as string ?? "(non-text header)";
        var here = string.IsNullOrEmpty(path) ? header : path + " > " + header;
        var isRuntimeManaged = runtimeManaged.Contains(menuItem);

        // A leaf with no traceable command is the dangerous shape: it does something, but the
        // inventory cannot name it. Parents legitimately have no command of their own, and neither
        // do runtime-filled containers nor the placeholder rows they hold while empty.
        var isLeaf = menuItem.Items.Count == 0;
        if (isLeaf
            && !isRuntimeManaged
            && !parentIsRuntimeManaged
            && (menuItem.Command == null || !namesByCommand.ContainsKey(menuItem.Command)))
        {
            untraceable.Add(here);
        }

        foreach (var child in menuItem.Items)
        {
            CollectUntraceable(child, here, namesByCommand, runtimeManaged, isRuntimeManaged, untraceable);
        }
    }

    /// <summary>
    /// Commands are matched by identity: the menu holds the very instance the VM property returns.
    /// Value equality would be wrong here — distinct commands must never collapse into one key.
    /// </summary>
    private sealed class CommandReferenceComparer : IEqualityComparer<ICommand>
    {
        public static readonly CommandReferenceComparer Instance = new();

        public bool Equals(ICommand? x, ICommand? y) => ReferenceEquals(x, y);

        public int GetHashCode(ICommand obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static Dictionary<ICommand, string> BuildCommandNameMap(MainViewModel vm)
    {
        var map = new Dictionary<ICommand, string>(CommandReferenceComparer.Instance);

        foreach (var property in typeof(MainViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(ICommand).IsAssignableFrom(property.PropertyType) || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            // [RelayCommand]-generated getters construct the command lazily; reading them is safe.
            if (property.GetValue(vm) is ICommand command)
            {
                map[command] = property.Name;
            }
        }

        return map;
    }
}
