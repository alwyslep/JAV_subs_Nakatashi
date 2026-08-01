using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace JavStt.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Drag & drop is how a library this size is actually loaded - nobody picks 1,800 files
        // through a dialog.
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        this.FindControl<Button>("BtnFiles")!.Click += async (_, _) => await PickFilesAsync();
        this.FindControl<Button>("BtnFolder")!.Click += async (_, _) => await PickFolderAsync();
        this.FindControl<Button>("BtnCopyLog")!.Click += async (_, _) => await CopyLogAsync();

        // ★Floors on both panes. A GridSplitter will happily drag either row to zero, and once the
        //   queue is zero-height there is nothing left to grab to bring it back.
        if (this.FindControl<GridSplitter>("QueueLogSplit")?.Parent is Grid grid && grid.RowDefinitions.Count == 3)
        {
            grid.RowDefinitions[0].MinHeight = 140;
            grid.RowDefinitions[2].MinHeight = 90;
        }

        Opened += (_, _) => RestoreWindowState();
        Closing += (_, _) => SaveWindowState();
    }

    private JavStt.Core.JavSttSettings? Settings => Vm?.Settings;

    /// <summary>
    /// Puts the window back where it was.
    ///
    /// ★Applied in Opened rather than the constructor: Position is meaningless before the window
    ///   has a platform handle, and setting it early is silently dropped.
    ///
    /// ★An off-screen restore is refused. A monitor that is no longer attached would otherwise put
    ///   the window somewhere unreachable, and the only fix a user has for that is deleting the
    ///   settings file - which they have no reason to guess at.
    /// </summary>
    private void RestoreWindowState()
    {
        var s = Settings;
        if (s == null)
        {
            return;
        }

        if (s.WindowWidth > 200 && s.WindowHeight > 200)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        if (s.WindowX is { } x && s.WindowY is { } y && IsOnAScreen(x, y))
        {
            Position = new PixelPoint((int)x, (int)y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (s.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (s.LogHeight > 60 &&
            this.FindControl<GridSplitter>("QueueLogSplit")?.Parent is Grid grid &&
            grid.RowDefinitions.Count == 3)
        {
            grid.RowDefinitions[2].Height = new GridLength(s.LogHeight, GridUnitType.Pixel);
        }
    }

    private bool IsOnAScreen(double x, double y)
        => Screens.All.Any(screen => screen.Bounds.Contains(new PixelPoint((int)x + 40, (int)y + 20)));

    private void SaveWindowState()
    {
        var s = Settings;
        if (s == null)
        {
            return;
        }

        s.WindowMaximized = WindowState == WindowState.Maximized;

        // ★Only record geometry from a normal window. Reading Width/Height while maximized stores
        //   the screen size, so restoring un-maximized would fill the display and look broken.
        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
            s.WindowX = Position.X;
            s.WindowY = Position.Y;
        }

        if (this.FindControl<GridSplitter>("QueueLogSplit")?.Parent is Grid grid && grid.RowDefinitions.Count == 3)
        {
            s.LogHeight = grid.RowDefinitions[2].ActualHeight;
        }

        Vm?.PersistSettings();
    }

    /// <summary>Log height the double-click restores - the same 200 the window opens with.</summary>
    private const double DefaultLogHeight = 200;

    private void OnSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is GridSplitter { Parent: Grid grid } && grid.RowDefinitions.Count == 3)
        {
            grid.RowDefinitions[2].Height = new GridLength(DefaultLogHeight, GridUnitType.Pixel);
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainViewModel? Vm => DataContext as MainViewModel;

    // The header pills double as the toggles for the sections they describe - the same shortcut the
    // sibling translator's header offers. Plain Borders rather than Buttons: a Button wrapping a
    // Border rendered as nothing in the first attempt, and a pill needs no press state anyway.
    private void OnKeyPillPressed(object? sender, PointerPressedEventArgs e) => Vm?.ToggleKeyCommand.Execute(null);

    private void OnOptionsPillPressed(object? sender, PointerPressedEventArgs e) => Vm?.ToggleOptionsCommand.Execute(null);

    // Avalonia 12 shape: DataTransfer + DataFormat.File, not the old IDataObject/DataFormats.
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm == null || !e.DataTransfer.Contains(DataFormat.File))
        {
            return;
        }

        var paths = e.DataTransfer.TryGetFiles()?
            .Select(f => f.Path.LocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (paths is { Count: > 0 })
        {
            Vm.AddPaths(paths);
        }
    }

    private async Task PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "비디오 파일 선택",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("비디오")
                {
                    Patterns = JavStt.Core.BatchRunner.VideoExtensions.Select(e => "*" + e).ToList(),
                },
            ],
        });

        AddLocalPaths(files);
    }

    private async Task PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "폴더 선택",
            AllowMultiple = true,
        });

        AddLocalPaths(folders);
    }

    private void AddLocalPaths(IReadOnlyList<IStorageItem> items)
    {
        var paths = items.Select(i => i.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        if (paths.Count > 0)
        {
            Vm?.AddPaths(paths);
        }
    }

    private async Task CopyLogAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (Vm != null && clipboard != null)
        {
            await clipboard.SetTextAsync(Vm.LogText);
        }
    }
}
