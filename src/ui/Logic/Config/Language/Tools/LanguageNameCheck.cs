namespace Nikse.SubtitleEdit.Logic.Config.Language.Tools;

/// <summary>
/// Strings for the name-consistency pass. Deliberately small: Stop, Reset to default,
/// Apply {0} fixes, No issues found, engine errors and the setup hint are reused from
/// <see cref="LanguageAiReview"/> rather than duplicated here.
/// </summary>
public class LanguageNameCheck
{
    public string Title { get; set; }
    public string MenuItem { get; set; }
    public string Info { get; set; }
    public string Run { get; set; }
    public string Working { get; set; }
    public string DoneXNamesYLines { get; set; }
    public string PinAccepted { get; set; }
    public string PinAcceptedInfo { get; set; }
    public string NotPinnable { get; set; }
    public string EditPromptTitle { get; set; }
    public string PromptInfo { get; set; }

    public LanguageNameCheck()
    {
        Title = "Check names";
        MenuItem = "Check names...";
        Info =
            "Looks for people whose name the subtitle spells more than one way, or spells by " +
            "translating what its characters mean. The whole file is read at once - a second " +
            "spelling can only be seen next to the first.";
        Run = "Check names";
        Working = "Reading the dialogue...";
        DoneXNamesYLines = "Done - {0} names, {1} lines to change";
        PinAccepted = "Remember these spellings for this series";
        PinAcceptedInfo =
            "Accepted spellings are added to the shared glossary, so the translator uses them for " +
            "the next film in the series. Only names whose original form is known can be saved.";
        NotPinnable = "original form unknown - fixes the file only";
        EditPromptTitle = "Edit name-check prompt";
        PromptInfo =
            "Instructions sent to the model. {language} is replaced with the subtitle language. " +
            "The transliteration rule and the answer format are always appended and cannot be removed.";
    }
}
