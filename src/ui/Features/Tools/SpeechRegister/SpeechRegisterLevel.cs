using Nikse.SubtitleEdit.Logic.Config;
using System;

namespace Nikse.SubtitleEdit.Features.Tools.SpeechRegister;

/// <summary>
/// Korean speech levels (화계 / 상대높임법). Korean cannot avoid choosing one: every sentence
/// ending encodes the speaker-listener relationship, so a translation that ignores it is not
/// "neutral" - it silently picks one and sticks to it.
/// </summary>
public enum SpeechLevel
{
    /// <summary>하십시오체 - formal deferential (-습니다 / -습니까).</summary>
    Deferential,

    /// <summary>해요체 - everyday polite (-아요 / -어요).</summary>
    Polite,

    /// <summary>해체 (반말) - intimate plain (-아 / -어 / -지).</summary>
    Casual,

    /// <summary>해라체 - plain declarative, narration and writing (-다 / -냐 / -어라).</summary>
    Plain,
}

public static class SpeechLevels
{
    public static readonly SpeechLevel[] All =
    {
        SpeechLevel.Deferential, SpeechLevel.Polite, SpeechLevel.Casual, SpeechLevel.Plain,
    };

    /// <summary>Stable token for settings and for the model's echo field - never localized.</summary>
    public static string Token(SpeechLevel level) => level switch
    {
        SpeechLevel.Deferential => "deferential",
        SpeechLevel.Polite => "polite",
        SpeechLevel.Casual => "casual",
        _ => "plain",
    };

    public static SpeechLevel Parse(string? token) => (token ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "deferential" => SpeechLevel.Deferential,
        "casual" => SpeechLevel.Casual,
        "plain" => SpeechLevel.Plain,
        _ => SpeechLevel.Polite,
    };

    public static string Display(SpeechLevel level)
    {
        var l = Se.Language.Tools.SpeechRegister;
        return level switch
        {
            SpeechLevel.Deferential => l.LevelDeferential,
            SpeechLevel.Polite => l.LevelPolite,
            SpeechLevel.Casual => l.LevelCasual,
            _ => l.LevelPlain,
        };
    }

    /// <summary>
    /// What the model is told the level means. Deliberately spells out the actual endings:
    /// a model that only sees "polite" will drift, but one that sees "-아요/-어요" has a target.
    /// English, because the rest of the system prompt is English and mixing hurts small models.
    /// </summary>
    public static string Describe(SpeechLevel level) => level switch
    {
        SpeechLevel.Deferential =>
            "하십시오체, the formal deferential style: statements end in -ㅂ니다/습니다, questions in " +
            "-ㅂ니까/습니까, requests in -십시오. Used to superiors, customers, and in announcements.",
        SpeechLevel.Polite =>
            "해요체, the everyday polite style: statements and questions end in -아요/-어요/-예요/-이에요. " +
            "Polite but not stiff - the default between adults who are not close.",
        SpeechLevel.Casual =>
            "해체 (반말), the intimate plain style: endings -아/-어/-지/-야, questions -아?/-어?/-니?. " +
            "Used between close friends, to children, and by a senior speaking down to a junior.",
        _ =>
            "해라체, the plain declarative style: statements end in -다/-는다, questions in -냐/-니, " +
            "commands in -아라/-어라. Used in narration, writing, and speaking to children.",
    };

    /// <summary>
    /// True when the two texts differ by more than their ending. Used to flag a suggestion that
    /// rewrote the sentence instead of only re-levelling it.
    /// ★This is the whole safety story for this feature: the model is asked to change the ending
    ///   and nothing else, so a changed stem is the one thing worth surfacing. The AI review
    ///   window's length-ratio warning (1.4/0.6) cannot be reused here - "가" -> "가십시오" is a
    ///   ratio of 4.0 and is exactly correct, so that rule would flag nearly every good change.
    /// </summary>
    public static bool StemChanged(string before, string after)
    {
        var a = Normalize(before);
        var b = Normalize(after);
        if (a.Length == 0 || b.Length == 0)
        {
            return true;
        }

        // Compare the head and let the tail be rewritten - that is what re-levelling does.
        // Short lines are almost all ending, so there is no head to judge: compare one syllable
        // and stay quiet. Silence beats a wrong flag, same rule as everywhere else here.
        var shorter = Math.Min(a.Length, b.Length);
        var keep = shorter <= ShortLineSyllables ? 1 : shorter - EndingSyllables;

        for (var i = 0; i < keep; i++)
        {
            // ★The last compared syllable is compared without its 받침. Korean attaches the
            //   ending's first consonant to the preceding syllable, so the stem's final letter
            //   changes with the level even though the stem did not:
            //       가 + -ㄹ게   = 갈게        가 + -ㅂ니다 = 갑니다
            //       알- + -았어  = 알았어      알- + -았습니다
            //   Comparing 갈 against 갑 as plain characters says "the stem changed", which is
            //   wrong and would flag the most ordinary conversion this tool performs.
            var last = i == keep - 1;
            var x = last ? StripFinalConsonant(a[i]) : a[i];
            var y = last ? StripFinalConsonant(b[i]) : b[i];
            if (x != y)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Below this many syllables the line is mostly ending; do not judge the stem.</summary>
    private const int ShortLineSyllables = 4;

    /// <summary>'-습니다' / '-십시오' are 3 syllables of pure ending.</summary>
    private const int EndingSyllables = 3;

    private const int HangulBase = 0xAC00;
    private const int HangulCount = 11172;
    private const int JongCount = 28;

    /// <summary>A composed Hangul syllable with its 받침 removed; anything else unchanged.</summary>
    private static char StripFinalConsonant(char ch)
    {
        var offset = ch - HangulBase;
        if (offset < 0 || offset >= HangulCount)
        {
            return ch;
        }

        return (char)(ch - offset % JongCount);
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch) && !char.IsPunctuation(ch) && !char.IsSymbol(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }
}
