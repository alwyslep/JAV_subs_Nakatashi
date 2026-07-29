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

namespace Nikse.SubtitleEdit.Features.Tools.NameCheck;

/// <summary>
/// ★Structurally parallel to <see cref="SpeechRegister.SpeechRegisterWindow"/> and
///   <see cref="AiReviewWindow"/>, on purpose - a fix in one should be obvious to port to the
///   others. Two things are missing here compared to those, and both are deliberate: there is no
///   progress bar because the pass is a single call over the whole file, and no category filter
///   because every row is the same kind of change.
/// </summary>
public class NameCheckWindow : Window
{
    public NameCheckWindow(NameCheckViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = Se.Language.Tools.NameCheck.Title;
        Width = 1024;
        Height = 700;
        MinWidth = 800;
        MinHeight = 480;
        CanResize = true;
        vm.Window = this;
        DataContext = vm;

        var lr = Se.Language.Tools.AiReview;
        var ln = Se.Language.Tools.NameCheck;

        // ---------- engine row (settings shared with AI review) ----------
        var comboEngine = UiUtil.MakeComboBox(vm.Engines, vm, nameof(vm.SelectedEngine))
            .WithAccessibleName(Se.Language.General.Engine);
        comboEngine.ItemTemplate = AiEngineCombo.ItemTemplate();

        var textBoxOllamaModel = UiUtil.MakeTextBox(220, vm, nameof(vm.OllamaModel))
            .WithAccessibleName(Se.Language.General.Model);
        textBoxOllamaModel.Bind(IsVisibleProperty, new Binding(nameof(vm.IsOllamaVisible)));

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

        var enginePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { comboEngine, comboLlamaCppModel, textBoxOllamaModel, panelOpenAiCompatible },
        };

        var buttonEditPrompt = UiUtil.MakeButton(lr.EditPrompt, vm.EditPromptCommand)
            .WithIconLeft("fa-solid fa-pen");

        var engineRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        engineRow.Add(enginePanel, 0, 0);
        engineRow.Add(buttonEditPrompt, 0, 2);

        // ---------- what this does, and the one option ----------
        var info = new TextBlock
        {
            Text = ln.Info,
            Opacity = 0.75,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        var checkBoxPin = new CheckBox { Content = ln.PinAccepted };
        checkBoxPin.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(vm.PinAccepted)) { Mode = BindingMode.TwoWay });
        ToolTip.SetTip(checkBoxPin, ln.PinAcceptedInfo);

        var optionRow = new StackPanel { Spacing = 4, Children = { info, checkBoxPin } };

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
                // ★The reason carries the pair the glossary would learn (原文 -> 표기), and says so
                //   when it cannot be learnt - "remember these" must not be a silent half-promise.
                new DataGridTextColumn
                {
                    Header = Se.Language.General.Reason,
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    Binding = new Binding(nameof(ReviewSuggestionItem.Reason)),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                },
            },
        };
        AutomationProperties.SetName(dataGrid, ln.Title);
        dataGrid.Bind(DataGrid.SelectedItemProperty, new Binding(nameof(vm.SelectedSuggestion)));
        _ = new DataGridCheckboxMultiSelect<ReviewSuggestionItem>(dataGrid,
            item => item.IsSelected, (item, v) => item.IsSelected = v);

        var borderGrid = UiUtil.MakeBorderForControlNoPadding(dataGrid);

        // ---------- bottom bar ----------
        var statusText = MakeBoundTextBlock(nameof(vm.StatusText));
        statusText.Opacity = 0.8;
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

        var buttonRun = UiUtil.MakeButton(ln.Run, vm.RunCommand).WithIconLeft("fa-solid fa-robot");
        buttonRun.Bind(IsVisibleProperty, new Binding(nameof(vm.IsNotRunning)));

        var buttonStop = UiUtil.MakeButton(lr.Stop, vm.StopCommand).WithIconLeft("fa-solid fa-stop");
        buttonStop.Bind(IsVisibleProperty, new Binding(nameof(vm.IsRunning)));

        var buttonApply = UiUtil.MakeButton(string.Empty, vm.ApplyCommand);
        buttonApply.Bind(ContentControl.ContentProperty, new Binding(nameof(vm.ApplyButtonText)));
        buttonApply.WithIconLeft("fa-solid fa-check");

        var buttonCancel = UiUtil.MakeButtonCancel(vm.CancelCommand);

        var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        statusRow.Add(new Optris.Icons.Avalonia.Icon
        {
            Value = "mdi-robot-outline",
            FontSize = 15,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        }, 0, 0);
        statusRow.Add(statusText, 0, 1);

        var bottomBar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        bottomBar.Add(leftButtons, 0, 0);
        bottomBar.Add(UiUtil.MakeButtonBar(buttonRun, buttonStop, buttonApply, buttonCancel), 0, 2);

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
        grid.Add(optionRow, 1, 0);
        grid.Add(borderGrid, 2, 0);
        grid.Add(statusRow, 3, 0);
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
