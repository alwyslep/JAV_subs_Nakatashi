using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Features.Tools.AiReview;
using Nikse.SubtitleEdit.Features.Translate;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.LlamaCpp;
using Nikse.SubtitleEdit.UiLogic.LlamaCpp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Tools.SpeechRegister;

/// <summary>
/// Matches the Korean speech level (화계) of the lines the user selected in the grid.
///
/// ★Modeled on <see cref="AiReviewViewModel"/> and sharing its engine settings, chunker, wire
///   protocol, client and suggestion item - but it is not a subclass and not a copy of the loop's
///   judgement. Two things had to differ, and both are the reason this exists separately:
///     ①The batch's read-only context must come from the whole subtitle, not from the selection
///       (see <see cref="SpeechRegisterChunkBuilder"/>).
///     ②"Large change" cannot be length-based here. AI review flags a length ratio above 1.4,
///       and "가" -> "가십시오" is 4.0 - correct and completely ordinary for this tool. What
///       matters instead is whether anything but the ending moved
///       (<see cref="SpeechLevels.StemChanged"/>).
/// </summary>
public partial class SpeechRegisterViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<string> _engines;
    [ObservableProperty] private string _selectedEngine;
    [ObservableProperty] private bool _isOllamaVisible;
    [ObservableProperty] private bool _isLlamaCppVisible;
    [ObservableProperty] private bool _isOpenAiCompatibleVisible;
    [ObservableProperty] private string _ollamaModel;
    [ObservableProperty] private string _openAiCompatibleUrl;
    [ObservableProperty] private string _openAiCompatibleModel;
    [ObservableProperty] private string _openAiCompatibleApiKey;
    [ObservableProperty] private ObservableCollection<LlamaCppModelDisplay> _llamaCppModels;
    [ObservableProperty] private LlamaCppModelDisplay? _selectedLlamaCppModel;

    [ObservableProperty] private ObservableCollection<string> _levels;
    [ObservableProperty] private string _selectedLevel;
    [ObservableProperty] private string _relationshipNote;

    [ObservableProperty] private ObservableCollection<ReviewSuggestionItem> _suggestions;
    [ObservableProperty] private ReviewSuggestionItem? _selectedSuggestion;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isNotRunning = true;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string _selectionText;
    [ObservableProperty] private string _summaryText;
    [ObservableProperty] private string _applyButtonText;
    [ObservableProperty] private string _warningNoteText;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }
    public Subtitle FixedSubtitle { get; private set; } = new();
    public int SelectedCount => _allSuggestions.Count(s => s.IsSelected);

    private readonly IWindowService _windowService;
    private readonly List<ReviewSuggestionItem> _allSuggestions = new();
    private Subtitle _subtitle = new();
    private readonly HashSet<int> _selectedNumbers = new();
    private string _languageCode = "ko";
    private CancellationTokenSource _cancellationTokenSource = new();
    private bool _syncingSelection;

    public SpeechRegisterViewModel(IWindowService windowService)
    {
        _windowService = windowService;

        // ★Engine settings are AI review's, deliberately. A second copy would mean configuring
        //   the same local model twice and drifting apart. The AI assistant already does this.
        var s = Se.Settings.Tools.AiReview;
        Engines = new ObservableCollection<string> { SeAiReview.EngineLlamaCpp, SeAiReview.EngineOllama, SeAiReview.EngineOpenAiCompatible };
        SelectedEngine = Engines.Contains(s.Engine) ? s.Engine : SeAiReview.EngineLlamaCpp;
        OllamaModel = s.OllamaModel;
        OpenAiCompatibleUrl = s.OpenAiCompatibleUrl;
        OpenAiCompatibleModel = s.OpenAiCompatibleModel;
        OpenAiCompatibleApiKey = s.OpenAiCompatibleApiKey;
        LlamaCppModels = new ObservableCollection<LlamaCppModelDisplay>();
        SelectedLlamaCppModel = LlamaCppDownloadHelper.PopulateModels(
            LlamaCppModels, LlamaCppServerManager.GetAllReviewModels(), s.LlamaCppModelFileName);

        Levels = new ObservableCollection<string>(SpeechLevels.All.Select(SpeechLevels.Display));
        SelectedLevel = SpeechLevels.Display(SpeechLevels.Parse(Se.Settings.Tools.SpeechRegister.Level));
        RelationshipNote = Se.Settings.Tools.SpeechRegister.RelationshipNote;

        Suggestions = new ObservableCollection<ReviewSuggestionItem>();
        StatusText = string.Empty;
        SelectionText = string.Empty;
        SummaryText = string.Empty;
        WarningNoteText = string.Empty;
        ApplyButtonText = string.Format(Se.Language.Tools.AiReview.ApplyXFixes, 0);
        UpdateSummary();
        UpdateEngineVisibility();
    }

    /// <param name="selectedIndices">0-based paragraph indices the user selected in the grid.</param>
    public void Initialize(Subtitle subtitle, IEnumerable<int> selectedIndices)
    {
        _subtitle = subtitle;
        _languageCode = LanguageAutoDetect.AutoDetectGoogleLanguage(subtitle);

        _selectedNumbers.Clear();
        foreach (var index in selectedIndices)
        {
            if (index >= 0 && index < subtitle.Paragraphs.Count)
            {
                _selectedNumbers.Add(index + 1);
            }
        }

        SelectionText = string.Format(Se.Language.Tools.SpeechRegister.SelectedLinesX, _selectedNumbers.Count);
    }

    private SpeechLevel CurrentLevel()
    {
        var index = Levels.IndexOf(SelectedLevel);
        return index >= 0 && index < SpeechLevels.All.Length ? SpeechLevels.All[index] : SpeechLevel.Polite;
    }

    private static string GetLanguageDisplayName(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    partial void OnSelectedEngineChanged(string value) => UpdateEngineVisibility();

    private void UpdateEngineVisibility()
    {
        IsOllamaVisible = SelectedEngine == SeAiReview.EngineOllama;
        IsLlamaCppVisible = SelectedEngine == SeAiReview.EngineLlamaCpp;
        IsOpenAiCompatibleVisible = SelectedEngine == SeAiReview.EngineOpenAiCompatible;
    }

    partial void OnIsRunningChanged(bool value) => IsNotRunning = !value;

    private void SaveSettings()
    {
        var s = Se.Settings.Tools.AiReview;
        s.Engine = SelectedEngine;
        s.OllamaModel = OllamaModel;
        s.OpenAiCompatibleUrl = OpenAiCompatibleUrl;
        s.OpenAiCompatibleModel = OpenAiCompatibleModel;
        s.OpenAiCompatibleApiKey = OpenAiCompatibleApiKey;
        if (SelectedLlamaCppModel != null)
        {
            s.LlamaCppModelFileName = SelectedLlamaCppModel.Model.FileName;
        }

        var r = Se.Settings.Tools.SpeechRegister;
        r.Level = SpeechLevels.Token(CurrentLevel());
        r.RelationshipNote = RelationshipNote ?? string.Empty;
    }

    [RelayCommand]
    private async Task Run()
    {
        if (IsRunning || Window == null || _selectedNumbers.Count == 0)
        {
            return;
        }

        SaveSettings();
        var lr = Se.Language.Tools.AiReview;
        var ls = Se.Language.Tools.SpeechRegister;

        string url;
        var model = string.Empty;
        string? apiKey = null;
        if (SelectedEngine == SeAiReview.EngineLlamaCpp)
        {
            var display = SelectedLlamaCppModel;
            if (display == null ||
                !await LlamaCppDownloadHelper.EnsureReadyAsync(Window, _windowService, display.Model.FileName,
                    LlamaCppServerManager.GetAllReviewModels(), persistAsTranslateModel: false))
            {
                RefreshLlamaCppModels();
                return;
            }

            RefreshLlamaCppModels();
            display = SelectedLlamaCppModel;
            if (display == null)
            {
                return;
            }

            IsRunning = true;
            StatusText = "llama.cpp...";
            try
            {
                await LlamaCppServerManager.EnsureServerRunningAsync(display.Model, CancellationToken.None);
            }
            catch (Exception e)
            {
                IsRunning = false;
                StatusText = string.Empty;
                await MessageBox.Show(Window, Se.Language.General.Error,
                    string.Format(lr.EngineError, e.Message), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            url = LlamaCppServerManager.ApiUrl;
        }
        else if (SelectedEngine == SeAiReview.EngineOpenAiCompatible)
        {
            url = OpenAiCompatibleUrl.Trim();
            model = OpenAiCompatibleModel.Trim();
            apiKey = string.IsNullOrWhiteSpace(OpenAiCompatibleApiKey) ? null : OpenAiCompatibleApiKey.Trim();
            IsRunning = true;
        }
        else
        {
            url = Se.Settings.Tools.AiReview.OllamaUrl;
            model = OllamaModel.Trim();
            IsRunning = true;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        ClearSuggestions();
        ProgressValue = 0;

        var lines = new List<ReviewLine>();
        for (var i = 0; i < _subtitle.Paragraphs.Count; i++)
        {
            var text = _subtitle.Paragraphs[i].Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(new ReviewLine(i + 1, text));
            }
        }

        var unitIds = AiReviewChunker.BuildUnitIds(lines);
        var unitIdByNumber = new Dictionary<int, int>();
        for (var i = 0; i < lines.Count; i++)
        {
            unitIdByNumber[lines[i].Number] = unitIds[i];
        }

        var chunks = SpeechRegisterChunkBuilder.Build(lines, _selectedNumbers, Se.Settings.Tools.SpeechRegister.MaxLinesPerBatch);
        var systemPrompt = SpeechRegisterPrompt.BuildSystemPrompt(
            SpeechRegisterPrompt.EffectivePrompt(), CurrentLevel(), RelationshipNote, GetLanguageDisplayName(_languageCode));

        using var client = new AiReviewClient();
        var processed = 0;
        var total = chunks.Sum(c => c.Lines.Count);
        var consecutiveErrors = 0;

        try
        {
            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                StatusText = string.Format(ls.WorkingLineXOfY, chunk.Lines[0].Number, _subtitle.Paragraphs.Count);

                var userContent = AiReviewProtocol.BuildUserContent(chunk);
                var editableNumbers = new HashSet<int>(chunk.Lines.Select(x => x.Number));

                List<AiReviewChange>? changes = null;
                try
                {
                    var reply = await client.ChatAsync(url, model, systemPrompt, userContent, ct, apiKey);
                    changes = AiReviewProtocol.ParseChanges(reply, editableNumbers);
                    if (changes.Count == 0 && AiReviewProtocol.ExtractJsonObject(reply) == null)
                    {
                        reply = await client.ChatAsync(url, model, systemPrompt, userContent, ct, apiKey);
                        changes = AiReviewProtocol.ParseChanges(reply, editableNumbers);
                    }

                    consecutiveErrors = 0;
                }
                catch (HttpRequestException e)
                {
                    consecutiveErrors++;
                    if (consecutiveErrors >= 3)
                    {
                        await MessageBox.Show(Window, Se.Language.General.Error,
                            string.Format(lr.EngineError, e.Message), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        break;
                    }
                }

                if (changes != null)
                {
                    foreach (var change in changes)
                    {
                        AddSuggestion(change, unitIdByNumber);
                    }
                }

                processed += chunk.Lines.Count;
                ProgressValue = Math.Min(100.0, processed * 100.0 / Math.Max(1, total));
            }

            StatusText = _allSuggestions.Count == 0 && processed >= total
                ? lr.NoIssuesFound
                : string.Format(ls.DoneXSuggestions, _allSuggestions.Count, processed);
        }
        catch (OperationCanceledException)
        {
            StatusText = string.Format(ls.DoneXSuggestions, _allSuggestions.Count, processed);
        }
        finally
        {
            ProgressValue = 100;
            IsRunning = false;
        }
    }

    private void RefreshLlamaCppModels()
    {
        SelectedLlamaCppModel = LlamaCppDownloadHelper.PopulateModels(
            LlamaCppModels, LlamaCppServerManager.GetAllReviewModels(), Se.Settings.Tools.AiReview.LlamaCppModelFileName);
    }

    private void ClearSuggestions()
    {
        _allSuggestions.Clear();
        Suggestions.Clear();
        WarningNoteText = string.Empty;
        UpdateSummary();
    }

    private void AddSuggestion(AiReviewChange change, Dictionary<int, int> unitIdByNumber)
    {
        var paragraphIndex = change.Number - 1;
        if (paragraphIndex < 0 || paragraphIndex >= _subtitle.Paragraphs.Count)
        {
            return;
        }

        // ★Only lines the user actually selected may be touched. ParseChanges already drops
        //   numbers outside the batch, but a batch also carries read-only context lines and a
        //   model that "helpfully" fixes one of those must not slip through.
        if (!_selectedNumbers.Contains(change.Number))
        {
            return;
        }

        var before = _subtitle.Paragraphs[paragraphIndex].Text;
        var after = change.NewText;
        if (SpeechRegisterPrompt.ShouldDrop(before, after))
        {
            return;
        }

        var ls = Se.Language.Tools.SpeechRegister;
        var isWarning = SpeechLevels.StemChanged(before, after);
        var reason = change.Reason;
        if (isWarning)
        {
            reason = string.IsNullOrEmpty(reason) ? ls.StemChangedWarning : $"{ls.StemChangedWarning} - {reason}";
        }

        var item = new ReviewSuggestionItem
        {
            Number = change.Number,
            ParagraphIndex = paragraphIndex,
            UnitId = unitIdByNumber.TryGetValue(change.Number, out var unitId) ? unitId : -change.Number,
            Category = ReviewCategory.Other,
            Before = before,
            After = after,
            Reason = reason,
            IsWarning = isWarning,
            IsSelected = !isWarning,
        };
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReviewSuggestionItem.IsSelected))
            {
                OnSuggestionSelectedChanged(item);
            }
        };

        _allSuggestions.Add(item);
        Suggestions.Add(item);
        UpdateSummary();
    }

    private void OnSuggestionSelectedChanged(ReviewSuggestionItem item)
    {
        if (_syncingSelection)
        {
            return;
        }

        // A sentence split across cues is re-levelled as a whole; accepting half of it would
        // leave the sentence with two different endings.
        _syncingSelection = true;
        try
        {
            foreach (var other in _allSuggestions)
            {
                if (other != item && other.UnitId == item.UnitId)
                {
                    other.IsSelected = item.IsSelected;
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var lr = Se.Language.Tools.AiReview;
        var selected = SelectedCount;
        SummaryText = string.Format(lr.XSuggestionsYSelected, _allSuggestions.Count, selected);
        ApplyButtonText = string.Format(lr.ApplyXFixes, selected);

        var warnings = _allSuggestions.Count(s => s.IsWarning);
        WarningNoteText = warnings > 0 ? string.Format(lr.XNeedACloserLook, warnings) : string.Empty;
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void Stop() => _cancellationTokenSource.Cancel();

    [RelayCommand]
    private void SelectAll() => SetAllSelected(true);

    [RelayCommand]
    private void SelectNone() => SetAllSelected(false);

    [RelayCommand]
    private void InvertSelection()
    {
        _syncingSelection = true;
        try
        {
            foreach (var item in _allSuggestions)
            {
                item.IsSelected = !item.IsSelected;
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdateSummary();
    }

    private void SetAllSelected(bool selected)
    {
        _syncingSelection = true;
        try
        {
            foreach (var item in _allSuggestions)
            {
                item.IsSelected = selected;
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        UpdateSummary();
    }

    [RelayCommand]
    private async Task PickOllamaModel()
    {
        if (Window == null)
        {
            return;
        }

        var result = await _windowService.ShowDialogAsync<Ocr.PickOllamaModelWindow, Ocr.PickOllamaModelViewModel>(Window, vm =>
        {
            vm.Initialize(Se.Language.General.PickOllamaModel, OllamaModel, Se.Settings.Tools.AiReview.OllamaUrl);
        });

        if (result is { OkPressed: true, SelectedModel: not null })
        {
            OllamaModel = result.SelectedModel;
        }
    }

    [RelayCommand]
    private void ResetPrompt()
    {
        Se.Settings.Tools.SpeechRegister.Prompt = SeSpeechRegister.DefaultPrompt;
    }

    [RelayCommand]
    private void Ok()
    {
        SaveSettings();

        FixedSubtitle = new Subtitle(_subtitle, false);
        foreach (var item in _allSuggestions.Where(s => s.IsSelected))
        {
            if (item.ParagraphIndex >= 0 && item.ParagraphIndex < FixedSubtitle.Paragraphs.Count)
            {
                FixedSubtitle.Paragraphs[item.ParagraphIndex].Text = item.After;
            }
        }

        OkPressed = true;
        _cancellationTokenSource.Cancel();
        Window?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource.Cancel();
        Window?.Close();
    }

    internal void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _cancellationTokenSource.Cancel();
            Window?.Close();
        }
    }

    internal void OnClosing()
    {
        _cancellationTokenSource.Cancel();
        UiUtil.SaveWindowPosition(Window);
    }
}
