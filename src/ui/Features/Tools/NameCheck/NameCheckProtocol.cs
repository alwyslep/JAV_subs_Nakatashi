using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Tools.AiReview;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Nikse.SubtitleEdit.Features.Tools.NameCheck;

/// <summary>One name as the model reports it: the original, the spelling to use, and what to fix.</summary>
public sealed record NameFinding(string Source, string Korean, IReadOnlyList<string> Wrong, string Reason);

/// <summary>One line the editor will change, worked out here rather than by the model.</summary>
public sealed record NameReplacement(int ParagraphIndex, int Number, string Before, string After, NameFinding Finding);

/// <summary>
/// Fork addition. The wire format for the name pass.
///
/// ★It asks for a name table, not for corrected lines - unlike AI review and the speech-level pass,
///   which both hand back rewritten text. Three reasons, and they all matter:
///     ①A pin needs the pair (original -> Korean spelling). Rewritten lines do not carry it, so the
///       glossary could never learn anything from this pass.
///     ②The editor does the substitution itself, so every before/after is exact and reviewable, and
///       the model cannot quietly reword a line while claiming to fix a name.
///     ③One pass over the dialogue instead of a rewrite per chunk - the model has to see the whole
///       file anyway to notice that two spellings are the same person.
///
/// ★Why a model at all: this was measured as a deterministic check first and it does not work.
///   Edit distance on Korean flags 어머니/할머니 and 아가씨/아저씨 as variants of each other, and a
///   series' canonical spelling appears in only 7.1% of the files whose series records it, so
///   absence carries no signal. Deciding that 유리 씨 and 유노 씨 are one person is a judgement.
/// </summary>
public static class NameCheckProtocol
{
    /// <summary>Same budget the sibling translator's prescan uses for a whole-file read.</summary>
    public const int MaxDialogueChars = 40000;

    public const string ProtocolText =
        "\n\nThe user message is the subtitle's dialogue, one line per line, in reading order.\n" +
        "Answer with ONLY a JSON object, no other text: {\"names\":[{\"src\":\"<name in the original " +
        "language>\",\"ko\":\"<the spelling to use>\",\"wrong\":[\"<other spelling used in the text>\"]," +
        "\"reason\":\"<short reason>\"}]}.\n" +
        "Rules for what to report:\n" +
        "- Report a name only when the text actually spells it more than one way, or spells it by " +
        "translating its meaning. A name that is written consistently needs no entry.\n" +
        "- \"wrong\" holds spellings that appear in the text VERBATIM. Never invent one, and never " +
        "put the chosen spelling in it.\n" +
        "- If you do not know the original-language form, leave \"src\" empty rather than guessing.\n" +
        "- If nothing needs fixing, answer {\"names\":[]}.";

    /// <summary>
    /// ★The transliteration rule is stated twice - in the instructions the user can edit, and here
    ///   in the part they cannot. It is the one rule of this pass, and a prompt edit that dropped it
    ///   would turn the tool into the thing it exists to undo.
    /// </summary>
    public const string TransliterationRule =
        "\n\nA personal name is TRANSLITERATED, never translated. Write what it sounds like, not what " +
        "its characters mean: 鈴の家りん is 스즈노야 린, not 린의 집 인; 蝦原 is 에비하라, not 새우하라. " +
        "If the text translated a name by meaning, that is exactly what you are here to report.";

    public static string BuildSystemPrompt(string instructions, string languageName, string? filmContext)
    {
        var prompt = (instructions ?? string.Empty).Replace("{language}", languageName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(filmContext))
        {
            prompt += "\n\n" + filmContext.Trim();
        }

        return prompt + TransliterationRule + ProtocolText;
    }

    /// <summary>
    /// The dialogue, one line per line, each distinct line once.
    ///
    /// ★Plain text, not the numbered JSON the other passes send: the model is not being asked to
    ///   point at lines, so numbering would only cost tokens.
    ///
    /// ★Repeats are dropped, and that is not a size trick - it is what this pass is looking at.
    ///   The question here is which spellings the file contains, so a line that appears twenty
    ///   times answers it exactly as well the first time. Measured on a 6,582-line subtitle: the
    ///   text hit the cap at line ~3,000 and everything after it was never examined, so a name
    ///   introduced late could not be found at all. Deduplicating removes that blind spot and
    ///   costs less rather than more.
    ///
    /// ★First-occurrence order is kept. The model still needs to see who is talking to whom to
    ///   decide whether two spellings are one person, and shuffling the file would take that away.
    /// </summary>
    public static string BuildUserContent(Subtitle subtitle)
    {
        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var paragraph in subtitle.Paragraphs)
        {
            var text = (paragraph.Text ?? string.Empty).Replace(Environment.NewLine, " ").Trim();
            if (text.Length == 0 || !seen.Add(text))
            {
                continue;
            }

            if (sb.Length + text.Length + 1 > MaxDialogueChars)
            {
                break;
            }

            sb.Append(text).Append('\n');
        }

        return sb.ToString();
    }

    public static List<NameFinding> ParseNames(string responseText)
    {
        var findings = new List<NameFinding>();
        var json = AiReviewProtocol.ExtractJsonObject(responseText);
        if (json == null)
        {
            return findings;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("names", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return findings;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var korean = ReadString(element, "ko");
            if (korean.Length == 0)
            {
                continue;
            }

            var wrong = new List<string>();
            if (element.TryGetProperty("wrong", out var wrongArray) && wrongArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in wrongArray.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = (item.GetString() ?? string.Empty).Trim();
                    // ★Dropping the chosen spelling out of its own "wrong" list is not pedantry:
                    //   left in, the replacement is a no-op that still shows as a suggestion, and the
                    //   user is asked to approve a change that changes nothing.
                    //
                    // ★And dropping anything CONTAINED IN the chosen spelling is what stops the text
                    //   being destroyed. Measured: the model proposed 사카미치 미루 as canonical with
                    //   사카미치 and 미루 among the wrong spellings. Replacing a string with something
                    //   that contains it feeds itself - one line came back as
                    //   "사카미치 사카미치 미루 사카미치 미루". It is also never a real finding: a
                    //   shorter form of the same name is an abbreviation, not a misspelling.
                    if (value.Length > 0 &&
                        !string.Equals(value, korean, StringComparison.Ordinal) &&
                        !korean.Contains(value, StringComparison.Ordinal) &&
                        KeepsFormOfAddress(value, korean) &&
                        !wrong.Contains(value))
                    {
                        wrong.Add(value);
                    }
                }
            }

            if (wrong.Count == 0)
            {
                continue;
            }

            findings.Add(new NameFinding(ReadString(element, "src"), korean, wrong, ReadString(element, "reason")));
        }

        return findings;
    }

    /// <summary>
    /// Works out which lines actually change. ★The model never gets to write a line here - it only
    /// says which spelling replaces which, and the substitution happens against the real text. A
    /// spelling the model imagined simply matches nothing and disappears.
    /// </summary>
    public static List<NameReplacement> BuildReplacements(Subtitle subtitle, IReadOnlyList<NameFinding> findings)
    {
        var replacements = new List<NameReplacement>();
        for (var i = 0; i < subtitle.Paragraphs.Count; i++)
        {
            var original = subtitle.Paragraphs[i].Text ?? string.Empty;
            if (original.Length == 0)
            {
                continue;
            }

            var text = original;
            NameFinding? applied = null;
            foreach (var finding in findings)
            {
                foreach (var wrong in finding.Wrong)
                {
                    if (text.Contains(wrong, StringComparison.Ordinal))
                    {
                        text = text.Replace(wrong, finding.Korean, StringComparison.Ordinal);
                        applied ??= finding;
                    }
                }
            }

            if (applied == null || string.Equals(text, original, StringComparison.Ordinal))
            {
                continue;
            }

            // ★Formatting tags must survive untouched - a name fix can never need to move one, and
            //   a replacement string that swallowed an <i> would corrupt the line silently.
            if (!AiReviewProtocol.TagsMatch(original, text))
            {
                continue;
            }

            replacements.Add(new NameReplacement(i, i + 1, original, text, applied));
        }

        return replacements;
    }

    /// <summary>
    /// Whether a finding may be written to the shared glossary.
    /// ★The same gate as everywhere else: a source already in Hangul went through a machine
    ///   translation, so it is not the original spelling of anything. Such a finding can still fix
    ///   the subtitle - it just may not teach the glossary.
    /// </summary>
    public static bool CanPin(NameFinding finding)
    {
        if (finding.Source.Length < 2 || finding.Korean.Length == 0)
        {
            return false;
        }

        foreach (var c in finding.Source)
        {
            if (c is >= '가' and <= '힣')
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] Honorifics = ["선생님", "짱", "씨", "군", "님", "상"];

    /// <summary>
    /// True when the replacement is the same form of address with only the error corrected.
    ///
    /// ★The substitution is word for word, so the form has to survive it. Measured: the model
    ///   answered 미르짱 -&gt; 사카미치 미루, and the lines came back "사카미치 미루이 그런 일은",
    ///   "사카미치 미루은, 반드시" - a full name dropped into a slot a nickname was holding, leaving
    ///   the particle attached to the wrong ending. Swapping 씨 for 군 fails here too, and rightly:
    ///   which honorific a speaker uses is the relationship, not a typo.
    /// </summary>
    internal static bool KeepsFormOfAddress(string wrong, string korean)
    {
        var wrongHonorific = TrailingHonorific(wrong);
        if (wrongHonorific.Length == 0)
        {
            // No honorific to preserve - only guard against a wholesale rewrite.
            return TrailingHonorific(korean).Length == 0;
        }

        if (korean.EndsWith(wrongHonorific, StringComparison.Ordinal))
        {
            return true;
        }

        // ★One exception, and only one: 상 is not a Korean honorific at all - it is さん left
        //   half-transliterated, and turning it into 씨 is the fix, not a change of relationship.
        //   Between two real Korean honorifics there is no exception: which one a speaker uses
        //   IS the relationship, so 군 -> 씨 is never a typo.
        return wrongHonorific == "상" && korean.EndsWith("씨", StringComparison.Ordinal);
    }

    private static string TrailingHonorific(string value)
    {
        foreach (var honorific in Honorifics)
        {
            if (value.EndsWith(honorific, StringComparison.Ordinal))
            {
                return honorific;
            }
        }

        return string.Empty;
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;
}
