using Nikse.SubtitleEdit.Core.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Tools.NameCheck;

/// <summary>
/// Fork addition. The same film's dialogue in its original language, lined up with the subtitle
/// being checked, so the name pass can learn what a name is actually written as.
///
/// ★Why this exists: the pin rate of the name pass is a function of whether the original form is
///   known, not of the model. Measured over eight real subtitles, half the surviving findings could
///   not be pinned because the model had no way to know the original spelling - it only ever saw the
///   translation. The film's original-language subtitle sits right next to it on disk 65.3% of the
///   time (749 of 1,147 Korean subtitles measured), so most of that gap is recoverable.
///
/// ★It never guesses. Everything here is measured, and where the measurement says "this file is not
///   the source", the answer is to use nothing rather than something:
///
///   - <b>Timecodes are preserved by translation, so a real match is exact.</b> Median share of cues
///     matching within 100 ms: 98%. Widening the window to 2 s does not raise that median at all -
///     it only lets wrong lines match. Hence <see cref="ToleranceSeconds"/> stays at 0.1.
///   - <b>A subtitle does not record which file it was translated from</b>, so the wrong original can
///     be picked. Measured: files that really are the source match 61-100% of cues; files that are a
///     different cut or a shifted copy match 1-11%. <see cref="MinimumMatchRate"/> sits in the empty
///     band between those two populations, and a file below it is discarded whole. Pulling the wrong
///     line and calling it the original would be worse than having no original at all.
///   - <b>Several original-language files usually means duplicates, not parts.</b> Of 230 films with
///     more than one, 218 (95%) had overlapping time spans - alternative rips of the whole film. So
///     one file is chosen and the rest ignored; concatenating them would stack two lines on one
///     timestamp. The 11 that do look like parts (P1/P2) turned out to carry a full-length copy
///     alongside, which <see cref="Pick"/> is written to prefer.
///   - <b>The language in the file name is a lie about a third of the time.</b> Measured over 400
///     files named <c>*.ja.srt</c>: 257 Japanese, <b>124 Chinese</b>, 16 romanised, 3 Korean - names
///     like <c>JUL-224.zh.ja.srt</c> and <c>JUR-268-zh-tw-繁中.ja.srt</c> exist. This one was caught
///     by a live run writing 希米卡 - a Chinese transliteration - over the correct 由美香. So the
///     content is checked, not the name: see <see cref="HasEnoughKana"/>.
/// </summary>
public sealed class OriginalDialogue
{
    /// <summary>How far apart two cues may start and still be the same line. Measured; see remarks.</summary>
    public const double ToleranceSeconds = 0.1;

    /// <summary>Below this share of matched cues the file is not the source and is discarded.</summary>
    public const double MinimumMatchRate = 0.5;

    /// <summary>
    /// Languages worth reading as "the original", best first.
    /// ★Only Japanese, deliberately. It is the source language of this library, it covers 65.3% of
    ///   the Korean subtitles on its own, and it is the only one whose spelling of a name IS the
    ///   original. A Chinese subtitle writes the name for a Chinese reader and an English one
    ///   romanises it, so both would widen coverage by feeding the glossary something that is not
    ///   the original form. Add one only with a measurement attached - and it will not be used until
    ///   <see cref="LooksLikeLanguage"/> can recognise it, which is deliberate: the file name cannot
    ///   be trusted, so a language with no content test would let anything through.
    /// </summary>
    public static readonly string[] PreferredLanguages = ["ja"];

    /// <summary>
    /// Share of CJK/letter characters that must be kana for text to be Japanese.
    /// ★Measured, not guessed: over 400 files the ratio is bimodal - 0-5% for 142 of them and 70-100%
    ///   for 256, with exactly one file anywhere in between. Anything from 0.2 to 0.6 separates the
    ///   two populations; 0.2 is chosen to leave room for a kanji-heavy Japanese subtitle.
    /// </summary>
    public const double MinimumKanaRatio = 0.2;

    private static readonly string[] SubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt"];

    private readonly long[] _startsMs;
    private readonly string[] _texts;

    private OriginalDialogue(string fileName, string languageCode, long[] startsMs, string[] texts, double matchRate)
    {
        FileName = fileName;
        LanguageCode = languageCode;
        MatchRate = matchRate;
        _startsMs = startsMs;
        _texts = texts;
    }

    public string FileName { get; }

    public string LanguageCode { get; }

    /// <summary>Share of the checked subtitle's cues that found a line here. Diagnostic, and the gate.</summary>
    public double MatchRate { get; }

    /// <summary>
    /// The original-language subtitle sitting next to <paramref name="videoFileName"/>, or null when
    /// there is none, none that parses, or none that lines up well enough to trust.
    /// </summary>
    public static OriginalDialogue? For(Subtitle? subtitle, string? videoFileName, string? subtitleLanguageCode)
    {
        if (subtitle == null || subtitle.Paragraphs.Count == 0 || string.IsNullOrWhiteSpace(videoFileName))
        {
            return null;
        }

        string directory;
        string stem;
        try
        {
            directory = Path.GetDirectoryName(videoFileName) ?? string.Empty;
            stem = Path.GetFileNameWithoutExtension(videoFileName);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (directory.Length == 0 || stem.Length == 0 || !Directory.Exists(directory))
        {
            return null;
        }

        foreach (var language in PreferredLanguages)
        {
            if (string.Equals(language, subtitleLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // ★Every candidate is tried, not just the best-looking one. A first attempt can fail on
            //   content (named .ja.srt, written in Chinese) or on alignment (a different cut), and in
            //   both cases the right file is often sitting in the same folder. Giving up after one
            //   would throw the feature away over a bad neighbour.
            foreach (var candidate in Candidates(directory, stem, language))
            {
                var loaded = Load(candidate);
                if (loaded == null)
                {
                    continue;
                }

                var (startsMs, texts) = loaded.Value;
                if (!LooksLikeLanguage(language, texts))
                {
                    continue;
                }

                var rate = MatchRateOf(subtitle, startsMs);
                if (rate < MinimumMatchRate)
                {
                    continue;
                }

                return new OriginalDialogue(candidate, language, startsMs, texts, rate);
            }
        }

        return null;
    }

    /// <summary>
    /// Files worth trying as the original, best first: plain <c>&lt;stem&gt;.&lt;lang&gt;.srt</c>, then
    /// most cues.
    /// ★Both rules point at the full-length copy in the measured cases where parts and a whole sat
    ///   side by side (858 cues beat 550 and 65; 1,417 beat 991).
    /// </summary>
    internal static List<string> Candidates(string directory, string stem, string language)
    {
        List<string> found;
        try
        {
            found = Directory.EnumerateFiles(directory, stem + "*")
                .Where(f => IsOriginalLanguageSubtitle(f, language))
                .ToList();
        }
        catch (IOException)
        {
            return new List<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return new List<string>();
        }

        if (found.Count < 2)
        {
            return found;
        }

        var plainName = stem + "." + language + ".srt";
        return found
            .Select(f => (File: f, Plain: string.Equals(Path.GetFileName(f), plainName, StringComparison.OrdinalIgnoreCase)))
            .Select(x => (x.File, x.Plain, Cues: Load(x.File)?.Starts.Length ?? 0))
            .OrderByDescending(x => x.Plain)
            .ThenByDescending(x => x.Cues)
            .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.File)
            .ToList();
    }

    /// <summary>
    /// Whether the text really is in <paramref name="language"/>.
    /// ★A language this cannot test is refused rather than assumed. Adding one to
    ///   <see cref="PreferredLanguages"/> without adding its test here therefore turns the feature off
    ///   for that language instead of silently accepting a mislabelled file - which is the failure this
    ///   whole check exists to stop.
    /// </summary>
    internal static bool LooksLikeLanguage(string language, IReadOnlyList<string> texts)
        => language switch
        {
            "ja" => HasEnoughKana(texts),
            _ => false,
        };

    /// <summary>
    /// ★Chinese writes a Japanese name in Chinese characters with no kana at all, so kana share is a
    ///   clean separator: measured 0% for the Chinese files and 70-100% for the Japanese ones. Latin
    ///   letters count towards the denominator so a romanised subtitle fails too.
    /// </summary>
    internal static bool HasEnoughKana(IReadOnlyList<string> texts)
    {
        var kana = 0;
        var counted = 0;
        foreach (var text in texts)
        {
            foreach (var c in text)
            {
                if (c is >= '぀' and <= 'ヿ')
                {
                    kana++;
                    counted++;
                }
                else if (c is (>= '一' and <= '鿿') or (>= '가' and <= '힣') || char.IsLetter(c))
                {
                    counted++;
                }
            }

            // A couple of thousand characters settles this; reading a whole film's dialogue does not
            // make the answer better.
            if (counted > 4000)
            {
                break;
            }
        }

        return counted > 0 && (double)kana / counted >= MinimumKanaRatio;
    }

    /// <summary>A subtitle file whose name carries the language code right before the extension.</summary>
    internal static bool IsOriginalLanguageSubtitle(string path, string language)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        if (!SubtitleExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.EndsWith("." + language + extension, StringComparison.OrdinalIgnoreCase);
    }

    private static (long[] Starts, string[] Texts)? Load(string fileName)
    {
        try
        {
            var loaded = Subtitle.Parse(fileName);
            if (loaded == null || loaded.Paragraphs.Count == 0)
            {
                return null;
            }

            var ordered = loaded.Paragraphs
                .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                .OrderBy(p => p.StartTime.TotalMilliseconds)
                .ToList();
            if (ordered.Count == 0)
            {
                return null;
            }

            return (ordered.Select(p => (long)Math.Round(p.StartTime.TotalMilliseconds)).ToArray(),
                    ordered.Select(p => Flatten(p.Text)).ToArray());
        }
        catch (Exception)
        {
            // ★Fail-soft on purpose: measured, 5 of 749 films carry an original-language file whose
            //   timecodes cannot be read at all. Losing the feature on those is correct; throwing is not.
            return null;
        }
    }

    private static double MatchRateOf(Subtitle subtitle, long[] startsMs)
    {
        var counted = 0;
        var matched = 0;
        foreach (var paragraph in subtitle.Paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph.Text))
            {
                continue;
            }

            counted++;
            if (Nearest(startsMs, (long)Math.Round(paragraph.StartTime.TotalMilliseconds)) >= 0)
            {
                matched++;
            }
        }

        return counted == 0 ? 0 : (double)matched / counted;
    }

    /// <summary>The original-language line at the same timestamp, or empty when there is none.</summary>
    public string TextAt(Paragraph? paragraph)
    {
        if (paragraph == null)
        {
            return string.Empty;
        }

        var index = Nearest(_startsMs, (long)Math.Round(paragraph.StartTime.TotalMilliseconds));
        return index < 0 ? string.Empty : _texts[index];
    }

    private static int Nearest(long[] startsMs, long targetMs)
    {
        if (startsMs.Length == 0)
        {
            return -1;
        }

        var toleranceMs = (long)Math.Round(ToleranceSeconds * 1000);
        var position = Array.BinarySearch(startsMs, targetMs);
        if (position >= 0)
        {
            return position;
        }

        var insert = ~position;
        var best = -1;
        var bestDistance = long.MaxValue;
        for (var i = insert - 1; i <= insert; i++)
        {
            if (i < 0 || i >= startsMs.Length)
            {
                continue;
            }

            var distance = Math.Abs(startsMs[i] - targetMs);
            if (distance <= toleranceMs && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static string Flatten(string text)
        => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
}
