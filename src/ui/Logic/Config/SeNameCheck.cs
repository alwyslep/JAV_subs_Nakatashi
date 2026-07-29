namespace Nikse.SubtitleEdit.Logic.Config;

/// <summary>
/// Settings for the name-consistency pass.
/// ★No engine settings here, on purpose - engine / URL / model / API key stay in
///   <see cref="SeAiReview"/> and are shared, exactly as the AI assistant and the speech-level
///   tool already share them.
/// </summary>
public class SeNameCheck
{
    public string Prompt { get; set; }

    /// <summary>Whether an accepted fix also teaches the shared glossary.</summary>
    public bool PinAcceptedNames { get; set; }

    public static string DefaultPrompt =>
        "You are checking the names in a {language} subtitle for one film.\n\n" +
        "Find every personal name, nickname and form of address in the dialogue, and report only " +
        "the ones the text does not spell consistently - the same person written two ways, or a " +
        "name written by translating what its characters mean instead of how it sounds.\n\n" +
        "Rules:\n" +
        "- Honorifics are part of the name here: 씨, 군, 짱, 님, 선생님. Treat \"사사키 씨\" and " +
        "\"사사키상\" as the same name spelled two ways.\n" +
        "- Two names that merely look similar are not the same person. 유리 and 유노 are different " +
        "people unless the dialogue shows otherwise; do not merge them to tidy the list.\n" +
        "- Ordinary words are not names. 어머니, 아저씨, 선생님 on their own are how people are " +
        "addressed, not who they are - report them only when attached to a name.\n" +
        "- Prefer the spelling the film already uses most, unless it is a meaning-translation.\n" +
        "- When in doubt, leave it out. A wrong merge renames a character for the whole file.";

    public SeNameCheck()
    {
        Prompt = DefaultPrompt;
        PinAcceptedNames = true;
    }
}
