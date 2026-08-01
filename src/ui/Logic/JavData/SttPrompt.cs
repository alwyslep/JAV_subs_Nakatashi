using Nikse.SubtitleEdit.Logic.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nikse.SubtitleEdit.Logic.JavData;

/// <summary>
/// Fork addition. Builds the per-film vocabulary prompt handed to a Whisper-family STT engine.
///
/// ★Why this exists at all, and it is not the reason you would guess. Whisper fabricates fluent
///   text when it is fed non-speech audio, and this library is mostly non-speech audio. Measured on
///   ABF-062, a 10-minute clip, three runs per condition: with no prompt the model emitted
///   <c>おやすみなさい</c> and <c>今日はご覧いただきありがとうございます</c> for 123.3 / 105.9 / 63
///   seconds - up to 16% of the clip. With a prompt: 0 / 0 / 0. The prompt is not a nicety here, it
///   is what makes the engine usable on this material.
///
/// ★The suppression comes from the seed sentence, NOT from the names. A generic prompt with no
///   cast name in it scored 0 / 0 / 0 as well, so do not assume a film with no recorded metadata is
///   unprotected - it still gets the seed. What the names buy is separate and additive: the
///   spellings the series has already settled on, applied at transcription time instead of being
///   repaired afterwards by the name-check pass.
///
/// ★Names go LAST. Whisper's decoder weights the tail of a long prompt more heavily, so the
///   ordering is seed → cast → address forms, weakest claim first. Address forms outrank cast on
///   purpose: <see cref="JavTerms"/> records that dialogue almost never uses a performer's legal
///   name (2 of 580 catalogue names were in the glossary), so what characters are actually called
///   is the more likely thing to hear.
/// </summary>
public static class SttPrompt
{
    /// <summary>
    /// ★Whisper caps the prompt at 224 tokens and Groq documents the same ceiling. The tokenizer
    ///   cannot be run from here, and Japanese lands near one token per character in Whisper's BPE,
    ///   so the budget is spent in characters with headroom rather than on a guess. Overrunning is
    ///   not a loud failure - the model silently keeps the tail, which would drop the seed and take
    ///   the hallucination guard with it. That asymmetry is why this is conservative.
    /// </summary>
    internal const int MaxCharacters = 180;

    /// <summary>How many recorded names are worth carrying before the budget is better spent elsewhere.</summary>
    internal const int MaxNames = 12;

    /// <summary>
    /// The prompt for the film <paramref name="videoFileName"/> holds, or an empty string when
    /// nothing could be added to <paramref name="configuredSeed"/>.
    ///
    /// ★<paramref name="configuredSeed"/> is the user's own setting and is never replaced, only
    ///   extended. Returning empty means "keep what is configured" - the caller must not overwrite
    ///   a setting with a blank.
    /// </summary>
    public static string Build(string? videoFileName, string? configuredSeed)
    {
        var seed = (configuredSeed ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(videoFileName))
        {
            return string.Empty;
        }

        var tags = VideoTagInfo.Read(videoFileName);
        var code = JavCatalog.ResolveCode(videoFileName, tags.Album);

        // ★Same gate, same reason as SpeakerContext: a cast list that lost even one entry to the
        //   Hangul test came from the machine-translation pass, and what survived survived only by
        //   having no Hangul to give it away. Take the catalogue's cast instead, or none.
        //   The fallback also covers the plain case of a file with no ©ART at all, so the two
        //   reasons to ask the catalogue share one lookup - it opens a connection.
        var cast = SpeakerContext.TrustedNames(tags.Performers);
        if (cast.Count == 0)
        {
            cast = SpeakerContext.TrustedNames(JavCatalog.Lookup(code)?.Performers ?? Array.Empty<string>());
        }

        var addressForms = new List<string>();
        foreach (var pair in JavTerms.AddressForms(JavDataPaths.SeriesPrefix(code)))
        {
            addressForms.Add(pair.Source);
        }

        return Assemble(seed, cast, addressForms);
    }

    /// <summary>
    /// Joins the parts into one Japanese line, dropping anything that cannot help and stopping at
    /// <see cref="MaxCharacters"/>.
    /// </summary>
    internal static string Assemble(string seed, IReadOnlyList<string> cast, IReadOnlyList<string> addressForms)
    {
        var names = new List<string>();
        // Address forms are appended after the cast so they land nearest the tail - see the type
        // remarks. Both lists are filtered and de-duplicated by the same pass.
        AddNames(names, cast);
        AddNames(names, addressForms);

        if (names.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (seed.Length > 0)
        {
            sb.Append(seed);
        }

        var written = 0;
        foreach (var name in names)
        {
            if (written == MaxNames)
            {
                break;
            }

            // "、" between names, and the whole run is closed with "。" below, so the block reads as
            // Japanese text rather than as a machine-readable list. Whisper's prompt is decoded as
            // preceding transcript, not as instructions.
            var addition = (written == 0 ? string.Empty : "、") + name;
            if (sb.Length + addition.Length + 1 > MaxCharacters)
            {
                break;
            }

            sb.Append(addition);
            written++;
        }

        if (written == 0)
        {
            return string.Empty;
        }

        sb.Append('。');
        return sb.ToString();
    }

    private static void AddNames(List<string> into, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var name = (candidate ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 24 || !IsUsableJapanese(name))
            {
                continue;
            }

            if (!into.Contains(name, StringComparer.Ordinal))
            {
                into.Add(name);
            }
        }
    }

    /// <summary>
    /// Whether a recorded name can bias a <b>Japanese</b> decode.
    ///
    /// ★Deliberately narrower than <see cref="JavTerms"/>'s pin gate, which accepts romanisation
    ///   and is right to: a glossary key and a decoder prompt are not the same object. 11.9% of the
    ///   glossary's usable rows are Latin-only (harvested from English subtitles), and "Takimoto"
    ///   in a Japanese prompt biases the decoder toward emitting Latin script in a Japanese
    ///   transcript - it makes the output worse, not better. Kana or kanji is the requirement here.
    ///
    /// ★Hangul is rejected for the fork's usual reason, and this is the second line of defence:
    ///   the cast list already passed <see cref="SpeakerContext.TrustedNames"/>, but glossary
    ///   address forms come from a different table with its own history.
    /// </summary>
    internal static bool IsUsableJapanese(string value)
    {
        var hasJapanese = false;
        foreach (var c in value)
        {
            // Hangul syllables, plus the Jamo block a decomposed character would land in.
            if (c is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ')
            {
                return false;
            }

            // ★A digit means the row is harvest debris, not a name - "6ファイルさん" is a real
            //   glossary entry for the ABF series. It would otherwise reach the prompt, where the
            //   budget is 12 names and every slot spent on debris is a real name left out.
            if (c is >= '0' and <= '9' or >= '０' and <= '９')
            {
                return false;
            }

            // Hiragana (incl. the prolonged sound mark at U+30FC via katakana), katakana,
            // and CJK unified ideographs.
            if (c is >= 'ぁ' and <= 'ゟ' or >= '゠' and <= 'ヿ' or >= '一' and <= '鿿')
            {
                hasJapanese = true;
            }
        }

        return hasJapanese;
    }
}
