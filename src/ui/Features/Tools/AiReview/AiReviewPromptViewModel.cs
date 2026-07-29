using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Logic.Config;
using System;

namespace Nikse.SubtitleEdit.Features.Tools.AiReview;

public partial class AiReviewPromptViewModel : ObservableObject
{
    [ObservableProperty] private string _promptText;

    // Nakatashi: 이 창을 화계 도구도 쓴다. 제목·설명·읽고 쓰는 자리를 인자로 받게만 바꿨다 —
    // 프롬프트 편집기를 하나 더 만들면 사본 2개가 갈리고, 한쪽만 고쳐지는 사고가 난다.
    [ObservableProperty] private string _titleText;
    [ObservableProperty] private string _infoText;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }

    private Func<string>? _load;
    private Action<string>? _save;
    private string _defaultPrompt = string.Empty;

    public AiReviewPromptViewModel()
    {
        PromptText = string.Empty;
        TitleText = Se.Language.Tools.AiReview.EditPromptTitle;
        InfoText = Se.Language.Tools.AiReview.PromptInfo;
    }

    public void Initialize()
    {
        var l = Se.Language.Tools.AiReview;
        Initialize(l.EditPromptTitle, l.PromptInfo,
            () => Se.Settings.Tools.AiReview.Prompt,
            value => Se.Settings.Tools.AiReview.Prompt = value,
            SeAiReview.DefaultPrompt);
    }

    public void Initialize(string title, string info, Func<string> load, Action<string> save, string defaultPrompt)
    {
        TitleText = title;
        InfoText = info;
        _load = load;
        _save = save;
        _defaultPrompt = defaultPrompt;

        var stored = load();
        PromptText = string.IsNullOrWhiteSpace(stored) ? defaultPrompt : stored;
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        PromptText = string.IsNullOrEmpty(_defaultPrompt) ? SeAiReview.DefaultPrompt : _defaultPrompt;
    }

    [RelayCommand]
    private void Ok()
    {
        var fallback = string.IsNullOrEmpty(_defaultPrompt) ? SeAiReview.DefaultPrompt : _defaultPrompt;
        var value = string.IsNullOrWhiteSpace(PromptText) ? fallback : PromptText.Trim();
        if (_save != null)
        {
            _save(value);
        }
        else
        {
            Se.Settings.Tools.AiReview.Prompt = value;
        }

        Se.SaveSettings();
        OkPressed = true;
        Window?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        Window?.Close();
    }

    internal void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Window?.Close();
        }
    }
}
