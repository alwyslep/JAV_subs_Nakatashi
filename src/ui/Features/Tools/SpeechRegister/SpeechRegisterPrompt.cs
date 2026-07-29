using Nikse.SubtitleEdit.Logic.Config;
using System;

namespace Nikse.SubtitleEdit.Features.Tools.SpeechRegister;

/// <summary>
/// Builds the system prompt for the speech-level pass.
///
/// ★<see cref="AiReview.AiReviewProtocol"/> is reused for everything below the prompt -
///   <c>BuildUserContent</c> (numbered JSON in), <c>ParseChanges</c> (hallucination and
///   context-line guard) and <c>TagsMatch</c> - and is <b>not modified</b>. Only the wording
///   is written here, because <c>AiReviewProtocol.BuildSystemPrompt</c> unconditionally appends
///   text that says "lines to review" / "actually need a correction" and names the five
///   proofreading categories. That is right for AI review and wrong here.
///   The wire shape below is deliberately identical to AI review's, so ParseChanges works
///   unchanged: only <c>n</c> and <c>text</c> are required, <c>category</c> is optional.
/// </summary>
public static class SpeechRegisterPrompt
{
    public const string ProtocolText =
        "\n\nThe user message is a JSON object. \"lines\" holds the subtitle lines to adjust, each with a line " +
        "number \"n\" and its \"text\" (a \"\\n\" inside a text is a line break inside that subtitle and must be " +
        "kept). \"context_before\" and \"context_after\" are read-only surrounding lines - use them to work out " +
        "who is speaking to whom, but never change or return those.\n" +
        "Answer with ONLY a JSON object, no other text: {\"changes\":[{\"n\":<line number>,\"text\":\"<full line " +
        "with the ending adjusted>\",\"reason\":\"<short reason>\"}]}. " +
        "Include only lines whose speech level actually has to change; if none do, answer {\"changes\":[]}.";

    public static string BuildSystemPrompt(string instructions, SpeechLevel level, string relationshipNote, string languageName)
    {
        var notes = string.IsNullOrWhiteSpace(relationshipNote)
            ? string.Empty
            : "Who speaks how in this film (follow this over the target level when a line clearly " +
              "belongs to one of these speakers):\n" + relationshipNote.Trim() + "\n";

        var prompt = (instructions ?? string.Empty)
            .Replace("{language}", languageName ?? string.Empty)
            .Replace("{level}", SpeechLevels.Describe(level))
            .Replace("{notes}", notes);

        return prompt + ProtocolText;
    }

    /// <summary>
    /// True when a suggestion should be dropped before the user ever sees it.
    /// ★Dropping silently is the right call for these three: they are the model failing the
    ///   contract, not a judgement the user should have to make row by row.
    /// </summary>
    public static bool ShouldDrop(string before, string after)
    {
        if (string.IsNullOrWhiteSpace(after))
        {
            return true;
        }

        if (string.Equals(before.Trim(), after.Trim(), StringComparison.Ordinal))
        {
            return true; // "changed" to the same thing
        }

        // Formatting tags must survive untouched - this is the one thing a re-levelling can never need.
        return !AiReview.AiReviewProtocol.TagsMatch(before, after);
    }

    public static string EffectivePrompt()
    {
        var stored = Se.Settings.Tools.SpeechRegister.Prompt;
        return string.IsNullOrWhiteSpace(stored) ? SeSpeechRegister.DefaultPrompt : stored;
    }
}
