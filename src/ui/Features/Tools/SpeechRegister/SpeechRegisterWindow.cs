using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Features.Files.Compare;
using Nikse.SubtitleEdit.Features.Main;
using Nikse.SubtitleEdit.Features.Tools.AiReview;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Tools.SpeechRegister;

/// <summary>
/// ★Deliberately a sibling of <see cref="AiReviewWindow"/> rather than an extension of the AI
///   assistant. The assistant's result contract is a single string (<c>ResultToApply</c> -&gt;
///   <c>current.Text</c>), and its root grid gives exactly one row a star height, so a per-line
///   accept/reject list cannot live there without re-indexing every row of an upstream file.
///   Keeping the two windows structurally parallel is on purpose: a fix in one should be
///   obvious to port to the other.
/// </summary>
public class SpeechRegisterWindow : Window
{
    public SpeechRegisterWindow(SpeechRegisterViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = Se.Language.Tools.SpeechRegister.Title;
        Width = 1024;
        Height = 720;
        MinWidth = 800;
        MinHeight = 500;
        CanResize = true;
        vm.Window = this;
        DataContext = vm;

        var lr = Se.Language.Tools.AiReview;
        var ls = Se.Language.Tools.SpeechRegister;

        // ---------- engine row (settings shared with AI review) ----------
        var comboEngine = UiUtil.MakeComboBox(vm.Engines, vm, nameof(vm.SelectedEngine))
            .WithAccessibleName(Se.Language.General.Engine);
        comboEngine.ItemTemplate = AiEngineCombo.ItemTemplate();

        var textBoxOllamaModel = UiUtil.MakeTextBox(220, vm, nameof(vm.OllamaModel))
            .WithAccessibleName(Se.Language.General.Model);
        var buttonPickOllamaModel = UiUtil.MakeButton("...", vm.PickOllamaModelCommand)
            .Compact()
            .WithAccessibleName(Se.Language.General.PickOllamaModel);
        var panelOllama = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { textBoxOllamaModel, buttonPickOllamaModel },
        };
        panelOllama.Bind(IsVisibleProperty, new Binding(nameof(vm.IsOllamaVisible)));

        var textBoxOpenAiUrl = UiUtil.MakeTextBox(250, vm, nameof(vm.OpenAiCompatibleUrl))
            .WithAccessibleName(Se.Language.General.Url);
        var textBoxOpenAiModel = UiUtil.MakeTextBox(150, vm, nameof(vm.OpenAiCompatibleModel))
            .WithAccessibleName(Se.Language.General.Model);
        textBoxOpenAiModel.PlaceholderText = Se.Language.General.Model;
        var textBoxOpenAiApiKey = UiUtil.MakeTextBox(130, vm, nameof(vm.OpenAiCompatibleApiKey))
            .WithAccessibleName(Se.Language.General.ApiKey);
        textBoxOpenAiApiKey.PlaceholderText = Se.Language.General.ApiKey;
        textBoxOpenAiApiKey.PasswordChar = '●';
        var panelOpenAiCompatible = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { textBoxOpenAiUrl, textBoxOpenAiModel, textBoxOpenAiApiKey },
        };
        panelOpenAiCompatible.Bind(IsVisibleProperty, new Binding(nameof(vm.IsOpenAiCompatibleVisible)));

        var comboLlamaCppModel = UiUtil.MakeComboBox(vm.LlamaCppModels, vm, nameof(vm.SelectedLlamaCppModel))
            .WithAccessibleName(Se.Language.General.Model);
        comboLlamaCppModel.ItemTemplate = StatusDots.ComboItemTemplate<Features.Translate.LlamaCppModelDisplay>(
            m => m.Model.DisplayName,
            m => string.IsNullOrEmpty(m.Model.Url)
                ? (string.IsNullOrEmpty(m.Model.Size) ? Se.Language.General.Custom : $"{Se.Language.General.Custom}, {m.Model.Size}")
                : (string.IsNullOrEmpty(m.Model.Size) ? null : m.Model.Size),
            m => m.IsInstalled ? DownloadDotStatus.UpToDate : DownloadDotStatus.NotInstalled);
        comboLlamaCppModel.Bind(IsVisibleProperty, new Binding(nameof(vm.IsLlamaCppVisible)));

        var selectionChip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0x4c, 0x9c, 0xe8)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x4c, 0x9c, 0xe8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new Optris.Icons.Avalonia.Icon { Value = "mdi-format-list-checks", FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                    MakeBoundTextBlock(nameof(vm.SelectionText)),
                },
            },
        };

        var enginePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { comboEngine, comboLlamaCppModel, panelOllama, panelOpenAiCompatible },
        };

        var engineRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        engineRow.Add(enginePanel, 0, 0);
        engineRow.Add(selectionChip, 0, 2);

        // ---------- speech level + relationship note ----------
        var comboLevel = UiUtil.MakeComboBox(vm.Levels, vm, nameof(vm.SelectedLevel))
            .WithAccessibleName(ls.Level);
        comboLevel.MinWidth = 200;

        var buttonEditPrompt = UiUtil.MakeButton(lr.EditPrompt, vm.EditPromptCommand)
            .WithIconLeft("fa-solid fa-pen");

        var levelPanel = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { UiUtil.MakeLabel(ls.Level), comboLevel },
                },
                buttonEditPrompt,
            },
        };

        // ★Free text, not a picker. The point of this box is that a film has more than one
        //   relationship, and only the person who watched it knows them. A description like
        //   "shy" is useless to the model - the hint text asks for direction and level.
        var textBoxNote = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 54,
            MaxHeight = 90,
            PlaceholderText = ls.RelationshipNoteWatermark,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        textBoxNote.Bind(TextBox.TextProperty, new Binding(nameof(vm.RelationshipNote)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(textBoxNote, ls.RelationshipNote);

        var noteInfo = new TextBlock
        {
            Text = ls.RelationshipNoteInfo,
            Opacity = 0.7,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        var notePanel = new StackPanel
        {
            Spacing = 4,
            Children = { UiUtil.MakeLabel(ls.RelationshipNote), textBoxNote, noteInfo },
        };

        var settingsRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 16,
        };
        settingsRow.Add(levelPanel, 0, 0);
        settingsRow.Add(notePanel, 0, 1);

        // ---------- suggestions grid ----------
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = double.NaN,
            Height = double.NaN,
            DataContext = vm,
            ItemsSource = vm.Suggestions,
            IsReadOnly = false,
            Columns =
            {
                new DataGridTemplateColumn
                {
                    Header = Se.Language.General.Apply,
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    CellTemplate = new FuncDataTemplate<ReviewSuggestionItem>((item, _) => new Border
                    {
                        Background = Brushes.Transparent,
                        Padding = new Thickness(4),
                        Child = new CheckBox
                        {
                            Focusable = false,
                            [!ToggleButton.IsCheckedProperty] = new Binding(nameof(ReviewSuggestionItem.IsSelected)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                    }),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                },
                new DataGridTextColumn
                {
                    Header = Se.Language.General.NumberSymbol,
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    Binding = new Binding(nameof(ReviewSuggestionItem.Number)),
                    IsReadOnly = true,
                },
                // ★No category column. Every row here is the same kind of change, so a column
                //   that always reads "Other" would be pure noise. The warning icon stays -
                //   that is the one distinction worth a glance.
                new DataGridTemplateColumn
                {
                    Header = string.Empty,
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    CellTemplate = new FuncDataTemplate<ReviewSuggestionItem>((item, _) =>
                    {
                        if (item is not { IsWarning: true })
                        {
                            return new Border();
                        }

                        var icon = new Optris.Icons.Avalonia.Icon
                        {
                            Value = "mdi-alert",
                            FontSize = 14,
                            Foreground = item.WarningBrush,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        ToolTip.SetTip(icon, item.Reason);
                        return new Border { Background = Brushes.Transparent, Padding = new Thickness(4), Child = icon };
                    }),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                },
                new DataGridTemplateColumn
                {
                    Header = Se.Language.General.Before,
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    CellTemplate = new FuncDataTemplate<ReviewSuggestionItem>((item, _) =>
                    {
                        if (item == null)
                        {
                            return new Border();
                        }

                        var (beforeBlock, _) = TextDiffHighlighter.CompareReplacement(item.Before, item.After);
                        return new Border { Background = Brushes.Transparent, Padding = new Thickness(4), Child = beforeBlock };
                    }),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                },
                new DataGridTemplateColumn
                {
                    Header = Se.Language.General.After,
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    CellTemplate = new FuncDataTemplate<ReviewSuggestionItem>((item, _) =>
                    {
                        if (item == null)
                        {
                            return new Border();
                        }

                        var (_, afterBlock) = TextDiffHighlighter.CompareReplacement(item.Before, item.After);
                        return new Border { Background = Brushes.Transparent, Padding = new Thickness(4), Child = afterBlock };
                    }),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                },
            },
        };
        AutomationProperties.SetName(dataGrid, ls.Title);
        dataGrid.Bind(DataGrid.SelectedItemProperty, new Binding(nameof(vm.SelectedSuggestion)));
        _ = new DataGridCheckboxMultiSelect<ReviewSuggestionItem>(dataGrid,
            item => item.IsSelected, (item, v) => item.IsSelected = v);

        var borderGrid = UiUtil.MakeBorderForControlNoPadding(dataGrid);

        // ---------- progress ----------
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
            [!RangeBase.ValueProperty] = new Binding(nameof(vm.ProgressValue)),
        };
        var statusText = MakeBoundTextBlock(nameof(vm.StatusText));
        statusText.Opacity = 0.8;

        var warningNote = MakeBoundTextBlock(nameof(vm.WarningNoteText));
        warningNote.Opacity = 0.85;
        warningNote.Margin = new Thickness(0, 0, 10, 0);

        var progressRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 10,
        };
        progressRow.Add(new Optris.Icons.Avalonia.Icon
        {
            Value = "mdi-robot-outline",
            FontSize = 15,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center,
        }, 0, 0);
        progressRow.Add(statusText, 0, 1);
        progressRow.Add(progressBar, 0, 2);
        progressRow.Add(warningNote, 0, 3);

        // ---------- bottom bar ----------
        var summaryText = MakeBoundTextBlock(nameof(vm.SummaryText));
        summaryText.Opacity = 0.8;

        var leftButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                summaryText.WithMarginRight(10),
                UiUtil.MakeButton(Se.Language.General.SelectAll, vm.SelectAllCommand),
                UiUtil.MakeButton(Se.Language.General.InvertSelection, vm.InvertSelectionCommand),
            },
        };

        var buttonRun = UiUtil.MakeButton(ls.Run, vm.RunCommand).WithIconLeft("fa-solid fa-robot");
        buttonRun.Bind(IsVisibleProperty, new Binding(nameof(vm.IsNotRunning)));

        var buttonStop = UiUtil.MakeButton(lr.Stop, vm.StopCommand).WithIconLeft("fa-solid fa-stop");
        buttonStop.Bind(IsVisibleProperty, new Binding(nameof(vm.IsRunning)));

        var buttonApply = UiUtil.MakeButton(string.Empty, vm.OkCommand);
        buttonApply.Bind(ContentControl.ContentProperty, new Binding(nameof(vm.ApplyButtonText)));
        buttonApply.WithIconLeft("fa-solid fa-check");

        var buttonCancel = UiUtil.MakeButtonCancel(vm.CancelCommand);

        var bottomBar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        bottomBar.Add(leftButtons, 0, 0);
        bottomBar.Add(UiUtil.MakeButtonBar(buttonRun, buttonStop, buttonApply, buttonCancel), 0, 2);

        // ---------- layout ----------
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
            },
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
            Margin = UiUtil.MakeWindowMargin(),
            RowSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        grid.Add(engineRow, 0, 0);
        grid.Add(settingsRow, 1, 0);
        grid.Add(borderGrid, 2, 0);
        grid.Add(progressRow, 3, 0);
        grid.Add(bottomBar, 4, 0);

        Content = grid;

        Loaded += delegate
        {
            buttonRun.Focus();
            UiUtil.RestoreWindowPosition(this);
        };
        Closing += delegate { vm.OnClosing(); };
        KeyDown += (_, e) => vm.OnKeyDown(e);
    }

    private static TextBlock MakeBoundTextBlock(string textPropertyPath)
    {
        var textBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        textBlock.Bind(TextBlock.TextProperty, new Binding(textPropertyPath));
        return textBlock;
    }
}
