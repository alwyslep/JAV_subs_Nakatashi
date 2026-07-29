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
using Nikse.SubtitleEdit.Logic.JavData;
using Nikse.SubtitleEdit.Logic.LlamaCpp;
using Nikse.SubtitleEdit.UiLogic.LlamaCpp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Tools.NameCheck;

/// <summary>
/// Finds people whose name the subtitle spells more than one way, or spells by translating what
/// its characters mean, and offers to make them consistent.
///
/// ★Modeled on <see cref="AiReview.AiReviewViewModel"/> and sharing its engine settings, client,
///   suggestion item and prompt editor - but the shape of the work is different in two ways that
///   are the reason it exists separately:
///     ①<b>One call over the whole file.</b> A second spelling can only be recognised next to the
///       first, so chunking would hide exactly what this is looking for.
///     ②<b>The model returns a name table, not rewritten lines.</b> The editor does the
///       substitution, which is what makes every suggestion an exact diff - and what produces the
///       (original, spelling) pair the shared glossary can be taught from.
/// </summary>
public partial class NameCheckViewModel : ObservableObject
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

    [ObservableProperty] private ObservableCollection<ReviewSuggestionItem> _suggestions;
    [ObservableProperty] private ReviewSuggestionItem? _selectedSuggestion;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isNotRunning = true;
    [ObservableProperty] private bool _pinAccepted;
    [ObservableProperty] private string _statusText;
    [ObservableProperty] private string _summaryText;
    [ObservableProperty] private string _applyButtonText;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }
    public Subtitle FixedSubtitle { get; private set; } = new();
    public int SelectedCount => _allSuggestions.Count(s => s.IsSelected);

    private readonly IWindowService _windowService;
    private readonly List<ReviewSuggestionItem> _allSuggestions = new();

    /// <summary>Which finding produced each suggestion, so an accepted one can teach the glossary.</summary>
    private readonly Dictionary<int, NameFinding> _findingByIndex = new();

    private Subtitle _subtitle = new();
    private string _languageCode = "ko";
    private string _seriesPrefix = string.Empty;
    private string _filmContext = string.Empty;
    private CancellationTokenSource _cancellationTokenSource = new();

    public NameCheckViewModel(IWindowService windowService)
    {
        _windowService = windowService;

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

        PinAccepted = Se.Settings.Tools.NameCheck.PinAcceptedNames;
        Suggestions = new ObservableCollection<ReviewSuggestionItem>();
        StatusText = string.Empty;
        SummaryText = string.Empty;
        ApplyButtonText = string.Format(Se.Language.Tools.AiReview.ApplyXFixes, 0);
        UpdateEngineVisibility();
    }

    public void Initialize(Subtitle subtitle, string? videoFileName = null)
    {
        _subtitle = subtitle;
        _languageCode = LanguageAutoDetect.AutoDetectGoogleLanguage(subtitle);

        // What the film itself says about its people - the original-language names are the only
        // way the model can tell a transliteration from a meaning-translation.
        var code = JavCatalog.ResolveCode(videoFileName);
        _seriesPrefix = JavDataPaths.SeriesPrefix(code);
        _filmContext = BuildFilmContext(videoFileName, code, _seriesPrefix);
    }

    private static string BuildFilmContext(string? videoFileName, string code, string seriesPrefix)
    {
        var parts = new List<string>();

        var cast = SpeakerContext.TrustedNames(Logic.Media.VideoTagInfo.Read(videoFileName).Performers);
        if (cast.Count == 0)
        {
            cast = SpeakerContext.TrustedNames(JavCatalog.Lookup(code)?.Performers ?? Array.Empty<string>());
        }

        if (cast.Count > 0)
        {
            parts.Add("People credited on this film, in the original language: " + string.Join(", ", cast));
        }

        var known = JavTerms.NamesInstruction(seriesPrefix);
        if (known.Length > 0)
        {
            parts.Add("Spellings this series has already settled - prefer them: " +
                      known[(known.IndexOf(':') + 1)..].Trim());
        }

        return string.Join("\n", parts);
    }

    partial void OnSelectedEngineChanged(string value) => UpdateEngineVisibility();

    partial void OnIsRunningChanged(bool value) => IsNotRunning = !value;

    private void UpdateEngineVisibility()
    {
        IsOllamaVisible = SelectedEngine == SeAiReview.EngineOllama;
        IsLlamaCppVisible = SelectedEngine == SeAiReview.EngineLlamaCpp;
        IsOpenAiCompatibleVisible = SelectedEngine == SeAiReview.EngineOpenAiCompatible;
    }

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

        Se.Settings.Tools.NameCheck.PinAcceptedNames = PinAccepted;
    }

    [RelayCommand]
    private async Task Run()
    {
        if (IsRunning || Window == null || _subtitle.Paragraphs.Count == 0)
        {
            return;
        }

        SaveSettings();
        var lr = Se.Language.Tools.AiReview;
        var ln = Se.Language.Tools.NameCheck;

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
        StatusText = ln.Working;

        var systemPrompt = NameCheckProtocol.BuildSystemPrompt(
            EffectivePrompt(), GetLanguageDisplayName(_languageCode), _filmContext);
        var userContent = NameCheckProtocol.BuildUserContent(_subtitle);

        try
        {
            using var client = new AiReviewClient();
            var reply = await client.ChatAsync(url, model, systemPrompt, userContent, ct, apiKey);
            var findings = NameCheckProtocol.ParseNames(reply);
            var replacements = NameCheckProtocol.BuildReplacements(_subtitle, findings);

            foreach (var replacement in replacements)
            {
                var item = new ReviewSuggestionItem
                {
                    Number = replacement.Number,
                    ParagraphIndex = replacement.ParagraphIndex,
                    UnitId = replacement.Number,
                    Category = ReviewCategory.Spelling,
                    Before = replacement.Before,
                    After = replacement.After,
                    Reason = BuildReason(replacement.Finding, ln),
                    IsSelected = true,
                };
                _allSuggestions.Add(item);
                _findingByIndex[replacement.ParagraphIndex] = replacement.Finding;
                Suggestions.Add(item);
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ReviewSuggestionItem.IsSelected))
                    {
                        UpdateSummary();
                    }
                };
            }

            var names = replacements.Select(r => r.Finding).Distinct().Count();
            StatusText = string.Format(ln.DoneXNamesYLines, names, replacements.Count);
            if (replacements.Count == 0)
            {
                StatusText = lr.NoIssuesFound;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = string.Empty;
        }
        catch (Exception e)
        {
            StatusText = string.Empty;
            await MessageBox.Show(Window, Se.Language.General.Error,
                string.Format(lr.EngineError, e.Message), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            IsRunning = false;
            UpdateSummary();
        }
    }

    /// <summary>★Says outright when a fix cannot be remembered, so "remember these" is never a
    /// promise the tool silently fails to keep.</summary>
    private string BuildReason(NameFinding finding, Logic.Config.Language.Tools.LanguageNameCheck ln)
    {
        var reason = finding.Reason;
        if (finding.Source.Length > 0)
        {
            reason = finding.Source + " -> " + finding.Korean + (reason.Length > 0 ? " - " + reason : string.Empty);
        }

        return NameCheckProtocol.CanPin(finding) ? reason : (reason + " (" + ln.NotPinnable + ")").Trim();
    }

    [RelayCommand]
    private void Stop() => _cancellationTokenSource.Cancel();

    [RelayCommand]
    private void Cancel()
    {
        if (IsRunning)
        {
            _cancellationTokenSource.Cancel();
            return;
        }

        Window?.Close();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in _allSuggestions)
        {
            item.IsSelected = true;
        }

        UpdateSummary();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var item in _allSuggestions)
        {
            item.IsSelected = !item.IsSelected;
        }

        UpdateSummary();
    }

    internal void OnClosing() => _cancellationTokenSource.Cancel();

    [RelayCommand]
    private async Task EditPrompt()
    {
        var ln = Se.Language.Tools.NameCheck;
        await _windowService.ShowDialogAsync<AiReviewPromptWindow, AiReviewPromptViewModel>(
            Window!, vm => vm.Initialize(
                ln.EditPromptTitle,
                ln.PromptInfo,
                EffectivePrompt,
                value => Se.Settings.Tools.NameCheck.Prompt = value,
                SeNameCheck.DefaultPrompt));
    }

    [RelayCommand]
    private void Apply()
    {
        if (Window == null)
        {
            return;
        }

        SaveSettings();
        var fixedSubtitle = new Subtitle(_subtitle);
        var accepted = _allSuggestions.Where(s => s.IsSelected).ToList();
        foreach (var item in accepted)
        {
            if (item.ParagraphIndex >= 0 && item.ParagraphIndex < fixedSubtitle.Paragraphs.Count)
            {
                fixedSubtitle.Paragraphs[item.ParagraphIndex].Text = item.After;
            }
        }

        // ★Only what the user actually accepted teaches the glossary, and only once per name.
        //   Pinning a rejected suggestion would carve a spelling the user just refused into data
        //   the translator then trusts for every later film in the series.
        if (PinAccepted && _seriesPrefix.Length > 0)
        {
            var pinned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in accepted)
            {
                if (!_findingByIndex.TryGetValue(item.ParagraphIndex, out var finding) ||
                    !NameCheckProtocol.CanPin(finding) ||
                    !pinned.Add(finding.Source))
                {
                    continue;
                }

                JavTerms.Pin(_seriesPrefix, finding.Source, finding.Korean);
            }
        }

        FixedSubtitle = fixedSubtitle;
        OkPressed = true;
        Window.Close();
    }

    private static string EffectivePrompt()
    {
        var stored = Se.Settings.Tools.NameCheck.Prompt;
        return string.IsNullOrWhiteSpace(stored) ? SeNameCheck.DefaultPrompt : stored;
    }

    private void ClearSuggestions()
    {
        _allSuggestions.Clear();
        _findingByIndex.Clear();
        Suggestions.Clear();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var count = SelectedCount;
        ApplyButtonText = string.Format(Se.Language.Tools.AiReview.ApplyXFixes, count);
        SummaryText = string.Format(Se.Language.Tools.AiReview.XSuggestionsYSelected, _allSuggestions.Count, count);
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

    internal void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }
}
