using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Tools.AiReview;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Nikse.SubtitleEdit.Features.Tools.NameCheck;

/// <summary>One name as the model reports it: the original, the spelling to use, and what to fix.</summary>
public sealed record NameFinding(string Source, string Korean, IReadOnlyList<string> Wrong, string Reason);

/// <summary>One line the editor will change, worked out here rather than by the model.</summary>
public sealed record NameReplacement(int ParagraphIndex, int Number, string Before, string After, NameFinding Finding);

/// <summary>
/// What the second pass says about one name once it has seen the original-language line: how the
/// original spells it, whether it is a name at all, whether the spelling the first pass chose is a
/// faithful reading of that original, and - when it is not - which spelling from the file is.
/// </summary>
public sealed record OriginalForm(string Source, bool IsName, bool ChosenSpellingFits, string Better);

/// <summary>
/// One name for the second pass: the spelling the first pass chose, the other spellings it found in
/// the file, and lines mentioning any of them next to the original-language line.
/// </summary>
public sealed record OriginalFormQuestion(
    string Korean,
    IReadOnlyList<string> Alternatives,
    IReadOnlyList<(string Translated, string Original)> Lines);

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
            var candidates = new List<string>();
            if (element.TryGetProperty("wrong", out var wrongArray) && wrongArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in wrongArray.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        candidates.Add(item.GetString() ?? string.Empty);
                    }
                }
            }

            var finding = MakeFinding(ReadString(element, "src"), korean, candidates, ReadString(element, "reason"));
            if (finding != null)
            {
                findings.Add(finding);
            }
        }

        return findings;
    }

    /// <summary>
    /// One finding, with the rules about what may be replaced applied. Returns null when nothing
    /// survives them - which is a finding that would change nothing, or would damage the text.
    ///
    /// ★This is the single place those rules live, because the direction of a finding can be reversed
    ///   later (see <see cref="SwapDirection"/>) and a reversed pair has to pass exactly the same
    ///   rules. Two copies of this would drift, and the copy that drifted would be the one writing to
    ///   the file.
    ///
    /// ★Dropping the chosen spelling out of its own "wrong" list is not pedantry: left in, the
    ///   replacement is a no-op that still shows as a suggestion, and the user is asked to approve a
    ///   change that changes nothing.
    ///
    /// ★Dropping anything CONTAINED IN the chosen spelling is what stops the text being destroyed.
    ///   Measured: the model proposed 사카미치 미루 as canonical with 사카미치 and 미루 among the wrong
    ///   spellings. Replacing a string with something that contains it feeds itself - one line came
    ///   back as "사카미치 사카미치 미루 사카미치 미루". It is also never a real finding: a shorter form
    ///   of the same name is an abbreviation, not a misspelling.
    /// </summary>
    public static NameFinding? MakeFinding(
        string source, string korean, IEnumerable<string> wrongCandidates, string reason)
    {
        if (korean.Length == 0)
        {
            return null;
        }

        var wrong = new List<string>();
        foreach (var candidate in wrongCandidates)
        {
            var value = (candidate ?? string.Empty).Trim();
            if (value.Length > 0 &&
                !string.Equals(value, korean, StringComparison.Ordinal) &&
                !korean.Contains(value, StringComparison.Ordinal) &&
                KeepsFormOfAddress(value, korean) &&
                !wrong.Contains(value))
            {
                wrong.Add(value);
            }
        }

        return wrong.Count == 0 ? null : new NameFinding(source, korean, wrong, reason);
    }

    /// <summary>
    /// Turns the finding round: <paramref name="better"/> becomes the spelling to use and the one the
    /// first pass chose joins the list of mistakes. Returns null when the swap is not usable.
    ///
    /// ★Why this exists: measured, the first pass gets the direction BACKWARDS often enough to matter.
    ///   On APNS-372 both of its findings did - 히노코리 씨 offered as correct with 히노보리 씨 as the
    ///   mistake while the original said ひのぼり, and 타키모스 씨 over 타키모토 씨 while the original
    ///   said 宅本. The original-language line settles it, and once it has, applying the fix in the
    ///   right direction is strictly better than refusing to apply it at all.
    ///
    /// ★<paramref name="better"/> must be one of the spellings the first pass actually reported. The
    ///   second pass is choosing between spellings that exist in the file, not inventing one - an
    ///   invented spelling would match no line and quietly do nothing, or worse, match the wrong one.
    /// </summary>
    public static NameFinding? SwapDirection(NameFinding finding, string? better)
    {
        var chosen = (better ?? string.Empty).Trim();
        if (chosen.Length == 0 ||
            string.Equals(chosen, finding.Korean, StringComparison.Ordinal) ||
            !finding.Wrong.Contains(chosen, StringComparer.Ordinal))
        {
            return null;
        }

        var candidates = finding.Wrong
            .Where(w => !string.Equals(w, chosen, StringComparison.Ordinal))
            .Append(finding.Korean);

        return MakeFinding(finding.Source, chosen, candidates, finding.Reason);
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

    // ─── The second pass: what the original language calls this person ────────────────────────────

    /// <summary>Names asked about in one request. Keeps the second call small and its answer readable.</summary>
    public const int MaxOriginalFormQuestions = 12;

    /// <summary>Line pairs shown per name. Three occurrences are plenty to read a spelling off.</summary>
    public const int MaxLinesPerQuestion = 3;

    public const string OriginalFormPrompt =
        "You are given names as a subtitle's translation spells them, and for each one the lines that " +
        "mention it - both the translated line and the SAME line in the film's original language, " +
        "matched by timestamp.\n\n" +
        "For each name, answer two things.\n" +
        "1. \"src\" - how the ORIGINAL language writes it, copied VERBATIM from the original line. If " +
        "the original line does not contain it, or you cannot tell which part of the line it is, leave " +
        "\"src\" empty. Never romanise it, never translate it, and never reconstruct it from the " +
        "translation - an invented original is worse than none.\n" +
        "2. \"isName\" - true only if this is a person: a given name, family name, nickname, or a name " +
        "with a form of address attached. false for anything else, including ordinary words, titles " +
        "with nobody attached, places, products and brands. A word that merely LOOKS like a name in " +
        "the translation is often a common word in the original - that is exactly what the original " +
        "line is here to settle.\n" +
        "3. \"fits\" - whether the name you were given is a faithful reading of the original: does it " +
        "sound like the original, syllable for syllable? Answer false when it does not, even slightly. " +
        "ひのぼり read as 히노코리 is false; ひのぼり read as 히노보리 is true.\n" +
        "4. \"better\" - ONLY when \"fits\" is false: which of the other spellings listed for that name " +
        "is a faithful reading of the original? Copy it exactly from the list you were given. If none " +
        "of them is, leave it empty. Never write a spelling that is not in the list - you are choosing " +
        "between the spellings the file actually contains, not proposing a new one.\n\n" +
        "Answer with ONLY a JSON object, no other text: " +
        "{\"names\":[{\"ko\":\"<the name as given to you, copied exactly>\",\"src\":\"<original form or " +
        "empty>\",\"isName\":true,\"fits\":true,\"better\":\"\"}]}. Include every name you were asked about.";

    public static string BuildOriginalFormRequest(IReadOnlyList<OriginalFormQuestion> questions)
    {
        var sb = new StringBuilder();
        foreach (var question in questions.Take(MaxOriginalFormQuestions))
        {
            sb.Append("NAME: ").Append(question.Korean).Append('\n');
            if (question.Alternatives.Count > 0)
            {
                sb.Append("  other spellings of it in the file: ")
                  .Append(string.Join(", ", question.Alternatives)).Append('\n');
            }

            foreach (var (translated, original) in question.Lines.Take(MaxLinesPerQuestion))
            {
                sb.Append("  translated: ").Append(translated).Append('\n');
                sb.Append("  original:   ").Append(original).Append('\n');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Keyed by the name as it was asked about. ★A reply about a name that was not asked is dropped by
    /// the caller looking up its own keys, so an invented entry cannot reach the glossary.
    /// </summary>
    public static Dictionary<string, OriginalForm> ParseOriginalForms(string responseText)
    {
        var result = new Dictionary<string, OriginalForm>(StringComparer.Ordinal);
        var json = AiReviewProtocol.ExtractJsonObject(responseText);
        if (json == null)
        {
            return result;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("names", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return result;
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

            // ★The two flags default in OPPOSITE directions, and the asymmetry is deliberate.
            //
            //   "isName" absent counts as FALSE: it gates a silent write into data the translator then
            //   trusts, so a malformed answer must not open that gate. The file still gets fixed.
            //
            //   "fits" absent counts as TRUE: it raises a visible objection to a suggestion the first
            //   pass made, and absence is no signal. Manufacturing an objection out of a missing field
            //   would unselect good suggestions every time a model skipped the key.
            var isName = element.TryGetProperty("isName", out var flag) && flag.ValueKind == JsonValueKind.True;
            var fits = !element.TryGetProperty("fits", out var fitsFlag) || fitsFlag.ValueKind != JsonValueKind.False;
            // ★"better" only means anything when the chosen spelling was rejected. Honouring it while
            //   "fits" is true would let a stray field reverse a finding nobody objected to.
            var better = fits ? string.Empty : ReadString(element, "better");
            result[korean] = new OriginalForm(ReadString(element, "src"), isName, fits, better);
        }

        return result;
    }

    /// <summary>
    /// The finding with the second pass's answer folded in.
    ///
    /// ★The second pass FILLS an unknown source; it never replaces a known one. Measured the hard way:
    ///   on a film whose video tag had already given the first pass 由美香, the second pass answered
    ///   希米卡 - a Chinese transliteration, read out of a subtitle that was named <c>.ja.srt</c> but
    ///   written in Chinese. The mislabelled file is refused elsewhere now, but the rule stands on its
    ///   own: the first pass's source comes from the film's own credits, and a second opinion drawn
    ///   from one line of dialogue is not grounds to overwrite that.
    ///
    /// ★A source the original line does not actually contain is refused: the instructions forbid
    ///   reconstructing one, and this is where that is enforced rather than trusted.
    /// </summary>
    public static NameFinding WithOriginalForm(NameFinding finding, OriginalForm form, IEnumerable<string> originalLines)
    {
        var source = TrimPunctuation(form.Source);
        if (source.Length == 0)
        {
            return finding;
        }

        // ★"Never replace a known source" has ONE exception, and the live run found it: when the second
        //   pass REJECTS the chosen spelling, the first pass's source is discredited too. Measured, the
        //   first pass answered src=Hinokori-san for a reading the original contradicts - a romanisation
        //   of the wrong reading. Keeping it while reversing the spelling would key the glossary on a
        //   spelling of a name nobody uses. When the first pass was right about the reading, its source
        //   still wins (that is the 由美香 / 希米卡 case).
        if (finding.Source.Length > 0 && form.ChosenSpellingFits)
        {
            return finding;
        }

        foreach (var line in originalLines)
        {
            if (line.Contains(source, StringComparison.Ordinal))
            {
                return finding with { Source = source };
            }
        }

        return finding;
    }

    /// <summary>
    /// ★Strips punctuation off the ends of a copied-out source. The model is told to copy verbatim and
    ///   it does - the live run returned <c>ひのぼりさん!</c>, exclamation mark and all, because that is
    ///   how the line ended. That mark would become part of the glossary key and match nothing ever
    ///   again. Letters of every script survive, so Japanese and Latin names are untouched.
    /// </summary>
    internal static string TrimPunctuation(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        var start = 0;
        var end = text.Length - 1;
        while (start <= end && !char.IsLetterOrDigit(text[start]))
        {
            start++;
        }

        while (end >= start && !char.IsLetterOrDigit(text[end]))
        {
            end--;
        }

        return end < start ? string.Empty : text[start..(end + 1)];
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
