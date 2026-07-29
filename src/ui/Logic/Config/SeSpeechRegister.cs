namespace Nikse.SubtitleEdit.Logic.Config;

/// <summary>
/// Settings for the speech-level (화계) tool.
/// ★No engine settings here on purpose. Engine / URL / model / API key stay in
///   <see cref="SeAiReview"/> and are shared, exactly as the AI assistant already shares them -
///   a second copy would drift and the user would have to configure the same local model twice.
/// </summary>
public class SeSpeechRegister
{
    /// <summary>Stable token, see <c>SpeechLevels.Token</c>. Not localized.</summary>
    public string Level { get; set; }

    /// <summary>Free text: "wife -&gt; neighbours: polite". Persisted because it is per-film work.</summary>
    public string RelationshipNote { get; set; }

    public string Prompt { get; set; }

    /// <summary>
    /// Smaller than AI review's 15. Speech level is decided by who is talking to whom, so the
    /// read-only context around a batch matters more than raw throughput.
    /// </summary>
    public int MaxLinesPerBatch { get; set; }

    public static string DefaultPrompt =>
        "You are a Korean subtitle editor. Your only job is to fix the speech level (화계) of the " +
        "given {language} subtitle lines.\n\n" +
        "Target speech level: {level}\n" +
        "{notes}\n" +
        "Rules:\n" +
        "- Change ONLY the sentence ending and forms of address. Keep every word, name, number and " +
        "nuance exactly as it is. Do not rephrase, do not shorten, do not translate.\n" +
        "- Keep all formatting tags (like <i> or {\\an8}) and line breaks exactly as they are.\n" +
        "- A line that is already at the target level needs no change - leave it out.\n" +
        "- Interjections, moans, sound effects and single nouns carry no speech level. Leave them out.\n" +
        "- If you cannot tell who is speaking to whom, leave the line out. Silence is correct here; " +
        "a wrong guess is not.";

    public SeSpeechRegister()
    {
        Level = "polite";
        RelationshipNote = string.Empty;
        Prompt = DefaultPrompt;
        MaxLinesPerBatch = 10;
    }
}
