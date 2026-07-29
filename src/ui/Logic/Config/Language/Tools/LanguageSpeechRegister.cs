namespace Nikse.SubtitleEdit.Logic.Config.Language.Tools;

/// <summary>
/// Strings for the speech-level (화계) tool. Deliberately small: everything that AI review
/// already says in all 32 languages - Stop, Reset to default, Apply {0} fixes,
/// {0} suggestions - {1} selected, No issues found, Line {0}, engine errors and the setup hint -
/// is reused from <see cref="LanguageAiReview"/> instead of being duplicated here.
/// </summary>
public class LanguageSpeechRegister
{
    public string Title { get; set; }
    public string MenuItem { get; set; }
    public string Level { get; set; }
    public string LevelDeferential { get; set; }
    public string LevelPolite { get; set; }
    public string LevelCasual { get; set; }
    public string LevelPlain { get; set; }
    public string RelationshipNote { get; set; }
    public string RelationshipNoteWatermark { get; set; }
    public string RelationshipNoteInfo { get; set; }
    public string Run { get; set; }
    public string SelectedLinesX { get; set; }
    public string WorkingLineXOfY { get; set; }
    public string DoneXSuggestions { get; set; }
    public string StemChangedWarning { get; set; }
    public string EditPromptTitle { get; set; }
    public string PromptInfo { get; set; }

    public LanguageSpeechRegister()
    {
        Title = "Match speech level";
        MenuItem = "Match speech level...";
        Level = "Speech level";
        LevelDeferential = "하십시오체 (deferential)";
        LevelPolite = "해요체 (polite)";
        LevelCasual = "해체 (casual)";
        LevelPlain = "해라체 (plain)";
        RelationshipNote = "Who speaks how (optional)";
        RelationshipNoteWatermark = "wife -> neighbours: polite\nneighbours -> wife: casual, commanding";
        RelationshipNoteInfo =
            "Write the direction and the level, one per line. A description like \"shy\" is not a speech " +
            "level and the model cannot act on it.";
        Run = "Match selected lines";
        SelectedLinesX = "{0} lines selected";
        WorkingLineXOfY = "Matching line {0} of {1}...";
        DoneXSuggestions = "Done - {0} suggestions in {1} lines";
        StemChangedWarning = "More than the ending changed - check this one";
        EditPromptTitle = "Edit speech-level prompt";
        PromptInfo =
            "Instructions sent to the model. {language} is replaced with the subtitle language, " +
            "{level} with the chosen speech level, and {notes} with the relationship notes above.";
    }
}
