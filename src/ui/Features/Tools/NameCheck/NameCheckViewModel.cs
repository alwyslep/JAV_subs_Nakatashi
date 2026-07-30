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
    [ObservableProperty] private string _selectedReasonText = string.Empty;
    [ObservableProperty] private bool _isSelectedReasonVisible;
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

    /// <summary>
    /// Names the second pass judged not to be people. ★Keyed by the chosen spelling, the same key the
    /// second pass answers on, and consulted only at pin time - the line fix still stands, because two
    /// spellings of one word are still worth making consistent.
    /// </summary>
    private readonly HashSet<string> _notAPerson = new(StringComparer.Ordinal);

    /// <summary>
    /// Names where the original language does not support the spelling the first pass chose.
    /// ★Measured: the model offered 히노코리 씨 as canonical and 히노보리 씨 as the mistake, while the
    ///   original said ひのぼり - so the "fix" would have written the wrong reading over the right one,
    ///   and with the original form now filled in it would also have been remembered. These rows are
    ///   shown, explained and left UNCHECKED rather than hidden: the pass is still right that the file
    ///   spells one person two ways, and the user is the one who can see which way is right.
    /// </summary>
    private readonly HashSet<string> _spellingDoubted = new(StringComparer.Ordinal);

    /// <summary>
    /// Names whose direction the original language reversed - the first pass had the mistake and the
    /// fix the wrong way round, and this is the corrected pair. Recorded so the row can say so: the
    /// user is being shown a suggestion that is the opposite of what the model first proposed.
    /// </summary>
    private readonly HashSet<string> _directionCorrected = new(StringComparer.Ordinal);

    /// <summary>
    /// Names the series' own glossary reversed, before any model was asked. Kept apart from
    /// <see cref="_directionCorrected"/> because the authority differs and the row should say which
    /// one spoke - and because a glossary decision is not re-opened by the model afterwards.
    /// </summary>
    private readonly HashSet<string> _glossaryCorrected = new(StringComparer.Ordinal);

    private Subtitle _subtitle = new();
    private string _languageCode = "ko";
    private string _seriesPrefix = string.Empty;
    private string _filmContext = string.Empty;
    private OriginalDialogue? _originalDialogue;
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

        // The same film in its original language, if it is on disk and its timecodes line up. This is
        // what lets a finding be pinned at all - see OriginalDialogue for why it refuses more than it
        // accepts.
        _originalDialogue = OriginalDialogue.For(subtitle, videoFileName, _languageCode);
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

    /// <summary>★The reason is where this window explains itself, and in the grid it is one clipped
    /// line - the first live run showed "流川夕 → 루카와 유 - The surname 루카와 w…" with no way to read
    /// the rest.</summary>
    partial void OnSelectedSuggestionChanged(ReviewSuggestionItem? value)
    {
        SelectedReasonText = value?.Reason ?? string.Empty;
        IsSelectedReasonVisible = SelectedReasonText.Length > 0;
    }

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

            // ★The second pass runs BEFORE the substitution is worked out, because it can reverse a
            //   finding's direction - and a replacement built from the wrong direction would have to be
            //   thrown away and rebuilt. It also means the model is shown the file's real lines rather
            //   than lines this tool has already corrected, which is what it needs to judge them.
            var resolved = await ResolveOriginalFormsAsync(findings, url, model, apiKey, ct);
            var replacements = NameCheckProtocol.BuildReplacements(_subtitle, resolved);

            foreach (var replacement in replacements)
            {
                var finding = replacement.Finding;
                var item = new ReviewSuggestionItem
                {
                    Number = replacement.Number,
                    ParagraphIndex = replacement.ParagraphIndex,
                    UnitId = replacement.Number,
                    Category = ReviewCategory.Spelling,
                    Before = replacement.Before,
                    After = replacement.After,
                    Reason = BuildReason(finding, ln),
                    IsSelected = !_spellingDoubted.Contains(finding.Korean),
                };
                _allSuggestions.Add(item);
                _findingByIndex[replacement.ParagraphIndex] = finding;
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

    /// <summary>
    /// Asks the original-language subtitle what these names are really written as, and whether they
    /// are names at all.
    ///
    /// ★Why a second call rather than more instructions in the first: the first pass only ever sees
    ///   the translation, and no prompt can make it read a spelling it was never shown. Measured, half
    ///   the findings that survived the guards could not be pinned for exactly that reason.
    ///
    /// ★Why it also classifies: filling in the original form REMOVES the accident that was protecting
    ///   the glossary. A measured case - 베피슨 / 베핀 - is べっぴん, "a beauty", misheard by the ASR;
    ///   the merge was right and the category was wrong, and the only thing that kept a common noun
    ///   out of a name glossary was the empty source. With the source filled in, that gate opens. So
    ///   the same call that opens it is the one asked to close it, and it can answer well because it
    ///   is looking at the original word rather than at a transliteration of it.
    ///
    /// Returns findings keyed by their chosen spelling. Everything here is fail-soft: no
    /// original-language subtitle, a refused one, an unparsable reply or a dropped connection all leave
    /// the findings exactly as the first pass produced them.
    /// </summary>
    private async Task<List<NameFinding>> ResolveOriginalFormsAsync(
        IReadOnlyList<NameFinding> findings, string url, string model, string? apiKey, CancellationToken ct)
    {
        var resolved = findings.ToList();

        // ★The series' own glossary gets asked first, because it is free, deterministic, and was
        //   measured to be right where both the model and the original-language subtitle were wrong.
        //   See JavTerms.RankSpellings for the case.
        for (var i = 0; i < resolved.Count; i++)
        {
            var swapped = ApplyGlossaryPreference(resolved[i]);
            if (swapped != null)
            {
                resolved[i] = swapped;
                _glossaryCorrected.Add(swapped.Korean);
            }
        }

        if (_originalDialogue == null || resolved.Count == 0)
        {
            return resolved;
        }

        var questions = new List<OriginalFormQuestion>();
        foreach (var finding in resolved.Take(NameCheckProtocol.MaxOriginalFormQuestions))
        {
            var spellings = new List<string> { finding.Korean };
            spellings.AddRange(finding.Wrong);

            // ★One line per spelling, round-robin - NOT the first few lines in file order. Measured, and
            //   it decides the answer: on APNS-372 the original spells one name both 宅本 (the real
            //   name) and タキモス (the ASR mangling it). Taking the first three matching lines showed
            //   only the タキモス ones, so the model confirmed the mangled reading and the tool went on to
            //   write it over the correct 타키모토. Shown a line for each spelling, it sees both and can
            //   tell which reading the original actually supports.
            var lines = new List<(string Translated, string Original)>();
            var byspelling = spellings
                .Select(spelling => LinesFor(spelling).GetEnumerator())
                .ToList();
            try
            {
                var exhausted = false;
                while (lines.Count < NameCheckProtocol.MaxLinesPerQuestion && !exhausted)
                {
                    exhausted = true;
                    foreach (var source in byspelling)
                    {
                        if (lines.Count >= NameCheckProtocol.MaxLinesPerQuestion || !source.MoveNext())
                        {
                            continue;
                        }

                        exhausted = false;
                        if (!lines.Contains(source.Current))
                        {
                            lines.Add(source.Current);
                        }
                    }
                }
            }
            finally
            {
                foreach (var source in byspelling)
                {
                    source.Dispose();
                }
            }

            if (lines.Count > 0)
            {
                questions.Add(new OriginalFormQuestion(finding.Korean, finding.Wrong, lines));
            }
        }

        if (questions.Count == 0)
        {
            return resolved;
        }

        StatusText = Se.Language.Tools.NameCheck.CheckingOriginal;
        string answer;
        try
        {
            using var client = new AiReviewClient();
            answer = await client.ChatAsync(url, model,
                NameCheckProtocol.OriginalFormPrompt,
                NameCheckProtocol.BuildOriginalFormRequest(questions), ct, apiKey);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The first pass already produced usable suggestions; losing the second is not a failure
            // worth a dialog. It only means nothing gets pinned.
            return resolved;
        }

        var forms = NameCheckProtocol.ParseOriginalForms(answer ?? string.Empty);
        for (var i = 0; i < resolved.Count; i++)
        {
            var finding = resolved[i];
            var question = questions.FirstOrDefault(q => q.Korean == finding.Korean);
            if (question == null || !forms.TryGetValue(finding.Korean, out var form))
            {
                continue;
            }

            var updated = NameCheckProtocol.WithOriginalForm(
                finding, form, question.Lines.Select(l => l.Original));

            // ★A glossary decision is not re-opened by the model. The glossary is a record of how this
            //   series spells the name; the model is reading one line of a machine transcription that
            //   was measured to mis-hear it. The model still gets to fill the source and say whether
            //   this is a person.
            if (!form.ChosenSpellingFits && !_glossaryCorrected.Contains(finding.Korean))
            {
                // ★The original settled it, so apply the fix in the right direction rather than
                //   refusing to apply it. Only when the swap is not usable does the row fall back to
                //   being shown, explained and left unchecked.
                var swapped = NameCheckProtocol.SwapDirection(updated, form.Better);
                if (swapped != null)
                {
                    updated = swapped;
                    _directionCorrected.Add(updated.Korean);
                }
                else
                {
                    _spellingDoubted.Add(updated.Korean);
                }
            }

            if (!form.IsName)
            {
                _notAPerson.Add(updated.Korean);
            }

            resolved[i] = updated;
        }

        return resolved;
    }

    /// <summary>
    /// The finding turned round because the series glossary already uses one of the other spellings,
    /// or null when the glossary has no opinion - which is the common case.
    ///
    /// ★It only overrules when the evidence is one-sided: a pinned row, or strictly more rows than any
    ///   rival. The glossary is harvested by machine too, so a tie is not an argument - that is exactly
    ///   how タキモス got in there next to five rows saying 滝本.
    /// </summary>
    private NameFinding? ApplyGlossaryPreference(NameFinding finding)
    {
        if (_seriesPrefix.Length == 0)
        {
            return null;
        }

        var candidates = new List<string> { finding.Korean };
        candidates.AddRange(finding.Wrong);
        var ranked = JavTerms.RankSpellings(_seriesPrefix, candidates);
        if (ranked.Count == 0 || string.Equals(ranked[0].Spelling, finding.Korean, StringComparison.Ordinal))
        {
            return null;
        }

        // ★A pinned spelling needs no comparison - it is a person's decision, and it wins even when it
        //   is the only candidate the glossary has ever seen. Found by pinning 聖一郎 -> 세이이치로 and
        //   noticing that a "count >= 2" guard would have refused to correct 성일랑 back to it, because
        //   the wrong reading was in no glossary row to be counted against.
        //
        // ★An UNPINNED row still needs a rival to beat. "I have seen this spelling and never the other"
        //   is weak evidence when the glossary is machine-harvested - that is how タキモス got in there
        //   in the first place.
        var decisive = ranked[0].Pinned || (ranked.Count >= 2 && ranked[0].Rows > ranked[1].Rows);
        return decisive ? NameCheckProtocol.SwapDirection(finding, ranked[0].Spelling) : null;
    }

    /// <summary>Lines containing one spelling, each paired with the original-language line.</summary>
    private IEnumerable<(string Translated, string Original)> LinesFor(string spelling)
    {
        foreach (var paragraph in _subtitle.Paragraphs)
        {
            var text = paragraph.Text ?? string.Empty;
            if (text.Length == 0 || !text.Contains(spelling, StringComparison.Ordinal))
            {
                continue;
            }

            var original = _originalDialogue!.TextAt(paragraph);
            if (original.Length > 0)
            {
                yield return (text.Replace(Environment.NewLine, " "), original);
            }
        }
    }

    /// <summary>★Says outright when a fix cannot be remembered, so "remember these" is never a
    /// promise the tool silently fails to keep - and says WHICH reason, because "we do not know the
    /// original" and "this is not a person" call for different things from the user.</summary>
    private string BuildReason(NameFinding finding, Logic.Config.Language.Tools.LanguageNameCheck ln)
    {
        var reason = finding.Reason;
        if (finding.Source.Length > 0)
        {
            reason = finding.Source + " -> " + finding.Korean + (reason.Length > 0 ? " - " + reason : string.Empty);
        }

        if (_glossaryCorrected.Contains(finding.Korean))
        {
            reason = (reason + " (" + ln.GlossaryCorrected + ")").Trim();
        }
        else if (_directionCorrected.Contains(finding.Korean))
        {
            reason = (reason + " (" + ln.DirectionCorrected + ")").Trim();
        }

        if (_spellingDoubted.Contains(finding.Korean))
        {
            return (reason + " (" + ln.SpellingDoubted + ")").Trim();
        }

        if (_notAPerson.Contains(finding.Korean))
        {
            return (reason + " (" + ln.NotAPerson + ")").Trim();
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
                    // ★The original language said this is not a person. The line still gets fixed;
                    //   the glossary does not learn a common noun as somebody's name.
                    _notAPerson.Contains(finding.Korean) ||
                    // ★And it said this spelling is not what the original sounds like. The user can
                    //   still choose to apply it, but a spelling the original contradicts is never
                    //   written to the glossary - it would outlive every later attempt to fix it.
                    _spellingDoubted.Contains(finding.Korean) ||
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
        _notAPerson.Clear();
        _spellingDoubted.Clear();
        _directionCorrected.Clear();
        _glossaryCorrected.Clear();
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
