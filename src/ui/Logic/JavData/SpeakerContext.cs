using Nikse.SubtitleEdit.Logic.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nikse.SubtitleEdit.Logic.JavData;

/// <summary>Where a speaker-relationship note came from. Shown to the user - see the remarks on
/// <see cref="SpeakerContext"/> for why that matters.</summary>
public enum SpeakerContextSource
{
    None,

    /// <summary>A human wrote or corrected it. The best answer there is.</summary>
    GuidebookPinned,

    /// <summary>The translator's prescan derived it while translating this film.</summary>
    Guidebook,

    /// <summary>Assembled from the video file's own tags.</summary>
    VideoTags,

    /// <summary>Assembled from the shared catalogue, when the file carries no tags.</summary>
    Catalog,
}

public sealed class SpeakerContextResult
{
    public static readonly SpeakerContextResult Empty = new();

    /// <summary>Release code, or empty when the film could not be identified.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>The text to put in front of the model. Empty when nothing was found.</summary>
    public string Note { get; init; } = string.Empty;

    public SpeakerContextSource Source { get; init; } = SpeakerContextSource.None;

    public bool IsEmpty => Note.Length == 0;

    /// <summary>True when a human's version is on record and must not be silently replaced.</summary>
    public bool IsHumanWritten => Source == SpeakerContextSource.GuidebookPinned;

    /// <summary>
    /// Whether <see cref="Note"/> is actually per-speaker RULES, as opposed to evidence to derive them
    /// from.
    ///
    /// ★This distinction has to be visible to callers, and the first live run of the window is why.
    ///   The relationship box is a user-editable field whose contents get pinned to the film's
    ///   guidebook, and it was being pre-filled with whatever this returned - including the tag block,
    ///   whose first line is an English instruction to the model ("No per-speaker rules were recorded
    ///   for this film...") followed by a raw dump of title, genre and cast. Two things went wrong at
    ///   once: a Korean UI showed English boilerplate in an input box, and one keystroke from the user
    ///   would have saved that boilerplate as a PINNED guidebook - the one thing the translator's
    ///   prescan may never overwrite. Rules belong in the box; evidence belongs only in the prompt.
    /// </summary>
    public bool IsRules =>
        Source is SpeakerContextSource.GuidebookPinned or SpeakerContextSource.Guidebook;
}

/// <summary>
/// Fork addition. Works out who speaks to whom in the film a subtitle belongs to.
///
/// ★This is the point of the whole exercise. Korean forces a speech level on every sentence
///   ending, so a translated line cannot be checked without knowing the relationship behind it -
///   and that information is in the original language, which the Korean subtitle no longer has.
///   Until now the user had to type it in by hand, from memory, for every film.
///
/// ★Four sources, best first. The first three are things that already exist on disk; only the
///   last is inference:
///     ①a guidebook a human pinned - unbeatable, and it is what the translator will use too
///     ②a guidebook the translator's prescan derived while translating this very film
///     ③the video's own tags - the original-language synopsis names the relationships outright
///     ④the shared catalogue, for files whose tagger wrote nothing
///
/// ★The result is shown in the editable box, not hidden in the prompt. What the user sees is
///   exactly what the model gets, and they can fix it - which is the only way the pinned
///   guidebook in ① ever comes to exist.
/// </summary>
public static class SpeakerContext
{
    /// <summary>
    /// ★A guard on the assembled text, not on the guidebook: a human's note is theirs to make as
    ///   long as they like (the store caps it), but an assembled blob is only worth what it costs
    ///   in every batch's system prompt.
    /// </summary>
    internal const int MaxAssembledLength = 1200;

    public static SpeakerContextResult Resolve(string? videoFileName)
    {
        if (string.IsNullOrWhiteSpace(videoFileName))
        {
            return SpeakerContextResult.Empty;
        }

        var tags = VideoTagInfo.Read(videoFileName);
        var code = JavCatalog.ResolveCode(videoFileName, tags.Album);

        // ① and ② - what is already recorded about this film.
        var guidebook = JavGuidebook.Load(code);
        if (guidebook is { IsEmpty: false })
        {
            return new SpeakerContextResult
            {
                Code = code,
                Note = guidebook.Text,
                Source = guidebook.Pinned ? SpeakerContextSource.GuidebookPinned : SpeakerContextSource.Guidebook,
            };
        }

        // ③ - the file's own tags.
        // ★If the gate dropped even one name, this file was tagged by the machine-translation pass,
        //   and the entries that survived it survived only because they had no Hangul to give them
        //   away - "TECH" out of ["모모타 미츠키", …, "TECH"] is a studio credit, not a performer.
        //   So a single drop discredits the whole tag: take the catalogue's cast instead, and if it
        //   has none, print no cast at all. A wrong cast is worse than no cast, because the model
        //   will look for those people in the dialogue and reason from not finding them.
        var cast = TrustedNames(tags.Performers);
        if (cast.Count == 0 && tags.Performers.Count > 0)
        {
            cast = TrustedNames(JavCatalog.Lookup(code)?.Performers ?? Array.Empty<string>());
        }

        var fromTags = Assemble(
            tags.TitleOriginal.Length > 0 ? tags.TitleOriginal : tags.Title,
            tags.Genres,
            cast,
            tags.OriginalSynopsis,
            AddressForms(code));
        if (fromTags.Length > 0)
        {
            return new SpeakerContextResult { Code = code, Note = fromTags, Source = SpeakerContextSource.VideoTags };
        }

        // ④ - the catalogue, for the files whose tagger wrote nothing.
        var entry = JavCatalog.Lookup(code);
        if (entry != null)
        {
            var fromCatalog = Assemble(
                entry.TitleOriginal.Length > 0 ? entry.TitleOriginal : entry.Title,
                entry.Genres,
                TrustedNames(entry.Performers),
                entry.Description,
                AddressForms(code));
            if (fromCatalog.Length > 0)
            {
                return new SpeakerContextResult { Code = code, Note = fromCatalog, Source = SpeakerContextSource.Catalog };
            }
        }

        return new SpeakerContextResult { Code = code };
    }

    /// <summary>
    /// Builds the note from whatever of the four fields is present.
    ///
    /// ★The leading sentence is not decoration. The prompt introduces this block as "who speaks how
    ///   in this film", which is true of a guidebook and false of a synopsis - without the sentence
    ///   the model would be told that a plot summary is a set of speech-level rules. Saying outright
    ///   that no rules were recorded turns it back into what it is: evidence to reason from.
    ///
    /// ★Everything stays in its original language. The point of these fields is that they survived
    ///   in the language that still marks the relationships; translating them here would throw away
    ///   the only reason to read them.
    /// </summary>
    internal static string Assemble(
        string title,
        IReadOnlyList<string> genres,
        IReadOnlyList<string> performers,
        string synopsis,
        IReadOnlyList<JavTermPair>? addressForms = null)
    {
        var forms = addressForms ?? Array.Empty<JavTermPair>();

        // Title alone says too little to be worth a block; require something with substance in it.
        if (synopsis.Length == 0 && genres.Count == 0 && performers.Count == 0 && forms.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append("No per-speaker rules were recorded for this film. Work them out from its own description:");

        Append(sb, "title", title);
        Append(sb, "genre", string.Join(", ", genres.Take(12)));
        Append(sb, "cast", string.Join(", ", performers.Take(8)));
        Append(sb, "synopsis", synopsis);

        // ★Last, and worded as an instruction rather than a fact. Everything above is evidence to
        //   reason from; this is the one line the model must obey literally, because a name the
        //   series has already settled on must not be respelled by a pass that is only meant to be
        //   changing sentence endings. AI review is given the same sentence, from the same builder.
        if (forms.Count > 0)
        {
            Append(sb, JavTerms.NamesInstructionLabel,
                string.Join(", ", forms.Select(f => f.Source + " = " + f.Korean)));
        }

        var text = sb.ToString();
        return text.Length > MaxAssembledLength ? text[..MaxAssembledLength].TrimEnd() : text;
    }

    /// <summary>
    /// Address forms recorded for the series this release belongs to.
    /// ★Series, not film - a name is a fact about the language, so unlike a relationship it is safe
    ///   to carry across films of the same series (the glossary is built on that distinction).
    /// </summary>
    private static IReadOnlyList<JavTermPair> AddressForms(string code)
        => JavTerms.AddressForms(JavDataPaths.SeriesPrefix(code));

    /// <summary>
    /// Drops names that contain Hangul, keeping the original-script ones.
    ///
    /// ★This is the fork's hard rule about names, applied at the one place names leave the metadata
    ///   and enter a prompt: <b>a name must be a transliteration, never a translation</b>. Roughly a
    ///   fifth of this library was tagged by a machine-translation pass that read the characters for
    ///   their meaning - 鈴の家りん came out as "린의 집 인" (literally "Rin's house person") and 蝦原
    ///   as "새우하라" ("shrimp-hara"). Handing those to a model is worse than handing it nothing:
    ///   it will match them against the subtitle, fail, and reason from a cast that never existed.
    ///
    /// ★A Hangul test rather than a quality test, and deliberately blunt. The tags are Japanese in
    ///   the good case and Korean in the broken one, so the script is the tell. It mirrors the
    ///   translator's <c>meta.py::_ja_performers()</c>, which drops the same rows for the same
    ///   reason; kanji, kana and romaji all survive.
    /// </summary>
    /// <summary>
    /// The cast list, or nothing at all.
    ///
    /// ★All or nothing on purpose. A single dropped name means the whole list came from the
    ///   machine-translation pass, and whatever survived the gate survived only because it had no
    ///   Hangul to give it away - "TECH" out of ["모모타 미츠키", …, "TECH"] is a studio credit
    ///   presented as a performer. A wrong cast is worse than no cast: the model looks for those
    ///   people in the dialogue and reasons from not finding them.
    /// </summary>
    internal static IReadOnlyList<string> TrustedNames(IReadOnlyList<string> names)
    {
        var kept = KeepOriginalNames(names);
        return kept.Count == names.Count ? kept : Array.Empty<string>();
    }

    internal static IReadOnlyList<string> KeepOriginalNames(IReadOnlyList<string> names)
    {
        var kept = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (name.Length > 0 && !ContainsHangul(name))
            {
                kept.Add(name);
            }
        }

        return kept;
    }

    private static bool ContainsHangul(string value)
    {
        foreach (var c in value)
        {
            // Hangul syllables, plus the Jamo blocks a stray decomposed character would land in.
            if (c is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏')
            {
                return true;
            }
        }

        return false;
    }

    private static void Append(StringBuilder sb, string label, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        sb.Append('\n').Append(label).Append(": ").Append(trimmed.Replace("\r", string.Empty).Replace('\n', ' '));
    }
}
