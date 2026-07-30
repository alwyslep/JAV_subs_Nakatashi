using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Logic.JavData;

/// <summary>One glossary entry: how a name is written in the original, and how it is written in Korean.</summary>
public sealed class JavTermPair
{
    public string Source { get; init; } = string.Empty;
    public string Korean { get; init; } = string.Empty;
}

/// <summary>
/// Fork addition. Reads the shared glossary's <b>address forms</b> - the names characters are
/// actually called by.
///
/// ★Measured before building this, and it changed the design. Performer names are almost never in
///   the glossary (2 of 580 catalogue names matched) because the glossary is harvested from
///   dialogue, and dialogue does not use a performer's legal name. What it does hold is how the
///   characters address each other: 石上さとみ→이시가미 사토미, おしろさん→오시로 씨,
///   鳥町君→토리마치 군. Looking up the cast list would have quietly returned nothing.
///
/// ★Address forms are the right thing for this fork to read anyway. Korean marks the relationship
///   in the ending <b>and</b> in how you address someone, so 씨/군/님 is part of the speech level,
///   not decoration - and keeping the spelling stable across a series is exactly what the glossary
///   accumulates for.
///
/// ★Writing back pins the row. That column had to be added to the glossary first: the translator's
///   curate pass orders by <c>curated</c> but does not exclude curated rows, so once its backlog
///   drained it would re-review - and possibly demote - a spelling a human had chosen, and a
///   reharvest would overwrite it outright. <c>pinned</c> now blocks both, on both sides.
/// </summary>
public static class JavTerms
{
    internal const string TableName = "terms";

    /// <summary>Enough to steady the spellings without crowding out the rest of the prompt.</summary>
    internal const int MaxAddressForms = 24;

    /// <summary>
    /// Address forms recorded for a series, most recently seen first. Empty when the glossary has
    /// nothing for it - the common case for a series nobody has translated yet.
    /// </summary>
    public static IReadOnlyList<JavTermPair> AddressForms(string? seriesPrefix, int max = MaxAddressForms)
    {
        var prefix = (seriesPrefix ?? string.Empty).Trim();
        if (prefix.Length == 0)
        {
            return Array.Empty<JavTermPair>();
        }

        using var connection = JavDb.OpenRead(JavDataPaths.TermsDb);
        if (connection == null || !JavDb.HasTable(connection, TableName))
        {
            return Array.Empty<JavTermPair>();
        }

        // quality='ok' skips what the curate pass demoted; anchor rows are prompt style examples
        // rather than lookups and are excluded for the same reason the translator excludes them from
        // its own seed.
        //
        // ★A pinned row is read back whatever it looks like, and it is read back FIRST.
        //   The honorific filter below is a quality proxy - measured to keep this layer free of the
        //   machine-translation pollution the rest of the glossary carries. A row a person pinned is
        //   not a proxy for quality, it is the thing itself, so the proxy must not exclude it. Found
        //   by pinning 由美香 -> 유미카 and watching the editor fail to read back what it had just
        //   written, because a given name carries no honorific. Ordering pinned first also stops a
        //   person's choice being crowded out of the 24-row budget by harvested rows.
        const string PinnedAware =
            "select src, ko, pinned from " + TableName +
            " where series = $series and quality = 'ok' and anchor = 0 order by pinned desc, last_seen desc";
        // An older glossary has no pinned column. Losing the ordering is fine; losing the layer is not.
        const string Legacy =
            "select src, ko, 0 as pinned from " + TableName +
            " where series = $series and quality = 'ok' and anchor = 0 order by last_seen desc";

        foreach (var sql in new[] { PinnedAware, Legacy })
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$series", prefix);

                var found = new List<JavTermPair>();
                using var reader = command.ExecuteReader();
                while (reader.Read() && found.Count < max)
                {
                    var source = JavDb.GetText(reader, 0).Trim();
                    var korean = JavDb.GetText(reader, 1).Trim();
                    if (JavDb.GetBool(reader, 2) || IsAddressForm(source, korean))
                    {
                        found.Add(new JavTermPair { Source = source, Korean = korean });
                    }
                }

                return found;
            }
            catch
            {
                // Fall through to the legacy shape, then give up.
            }
        }

        return Array.Empty<JavTermPair>();
    }

    /// <summary>
    /// The one line a pass that is not supposed to be renaming anyone should be given, or an empty
    /// string when the series has no recorded spellings.
    ///
    /// ★Worded as an instruction, not as context. Everything else these prompts receive is evidence
    ///   to reason from; this is a constraint, because a name the series has already settled on must
    ///   survive a pass whose job is sentence endings or typos. Both callers - the speech-level pass
    ///   and AI review - format it identically so a spelling cannot mean one thing in one window and
    ///   something else in the other.
    /// </summary>
    public static string NamesInstruction(string? seriesPrefix)
    {
        var forms = AddressForms(seriesPrefix);
        return forms.Count == 0
            ? string.Empty
            : NamesInstructionLabel + ": " + string.Join(", ", forms.Select(f => f.Source + " = " + f.Korean));
    }

    internal const string NamesInstructionLabel = "how this series writes names - keep these spellings exactly";

    /// <summary>
    /// Records the spelling a human chose for a name in this series, and pins it so neither the
    /// translator's reharvest nor its curate pass can overwrite it.
    ///
    /// ★<paramref name="source"/> must be in the original script. A source that already contains
    ///   Hangul went through the machine-translation pass, so it is not the original spelling of
    ///   anything - pinning it would carve a mistranslation into the shared glossary, which is the
    ///   exact failure this fork is trying to undo. Same gate as
    ///   <c>termdb.pin_term()</c> and <c>meta._ja_performers()</c>.
    /// </summary>
    public static bool Pin(string? seriesPrefix, string? source, string? korean)
    {
        var prefix = (seriesPrefix ?? string.Empty).Trim();
        var src = (source ?? string.Empty).Trim();
        var ko = (korean ?? string.Empty).Trim();

        if (prefix.Length == 0 || src.Length == 0 || ko.Length == 0 || ko.Length > 200)
        {
            return false;
        }

        foreach (var c in src)
        {
            if (c is >= '가' and <= '힣')
            {
                return false;
            }
        }

        if (!JavDataPaths.CanWriteTermsDb)
        {
            return false;
        }

        using var connection = JavDb.OpenWrite(JavDataPaths.TermsDb);
        if (connection == null)
        {
            return false;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "insert into " + TableName + "(series, src, ko, pinned, quality, curated)" +
                " values($series, $src, $ko, 1, 'ok', datetime('now'))" +
                " on conflict(series, src) do update set" +
                "  ko = excluded.ko, pinned = 1, quality = 'ok', curated = datetime('now')," +
                "  last_seen = datetime('now')";
            command.Parameters.AddWithValue("$series", prefix);
            command.Parameters.AddWithValue("$src", src);
            command.Parameters.AddWithValue("$ko", ko);
            command.ExecuteNonQuery();
            return true;
        }
        catch
        {
            // ★An older glossary has no pinned column. Refusing is right: writing without the pin
            //   would leave the spelling to be overwritten later, which is worse than not saving.
            return false;
        }
    }

    /// <summary>
    /// Whether an entry names a person being addressed.
    ///
    /// ★An honorific test, not a name test, and that is deliberate: the glossary is mostly ordinary
    ///   dialogue ("일어나 봐", "팬티 벗어") and nothing distinguishes a proper noun in it. An
    ///   honorific suffix does - 石上さとみ has none but 佐々木さん does, and the ones with a suffix
    ///   are precisely the ones that carry a relationship. Measured over the glossary: 2,597 of
    ///   18,173 usable entries qualify, spread over 156 series, with no Korean-polluted source among
    ///   them - the filter turns out to select for quality as well.
    ///
    /// ★The length floors are not padding. Without them the bare honorifics match themselves
    ///   (君→너, 王様→왕님) and the Korean 상 matches any word ending in it (変→이상).
    /// </summary>
    /// <summary>How many glossary rows for this series back a spelling, and whether a person pinned it.</summary>
    public sealed record SpellingSupport(string Spelling, int Rows, bool Pinned);

    /// <summary>
    /// Which of several competing spellings this series already uses, judged by its own glossary.
    ///
    /// ★Why this beats asking a model: it is free, deterministic, and it was measured to be right where
    ///   the model was wrong. On APNS-372 the name pass could not decide between 타키모스 씨 and
    ///   타키모토 씨, and the original-language subtitle did not settle it either - that subtitle is
    ///   machine-transcribed and mis-heard the name two different ways (タキモス, and 宅本 which is not
    ///   even in the glossary). The glossary knew: 滝本 / 滝本さん / 滝本先生 / Takimoto / たぎもつさん
    ///   all say 타키모토, five rows to one for 타키모스. The right answer was already recorded.
    ///
    /// ★Honorifics are stripped before matching, because the glossary holds the same person as
    ///   타키모토, 타키모토 씨 and 타키모토 선생님 and all three are evidence for the same reading.
    ///
    /// ★A pinned row wins outright rather than by count - it is a person's decision, and one of those
    ///   outranks any number of harvested rows.
    ///
    /// Returns the candidates that have any support, best first. Empty when the glossary knows none of
    /// them, which is the common case and means "no opinion" rather than "they are all wrong".
    /// </summary>
    public static IReadOnlyList<SpellingSupport> RankSpellings(string? seriesPrefix, IEnumerable<string> candidates)
    {
        var prefix = (seriesPrefix ?? string.Empty).Trim();
        var wanted = candidates
            .Select(c => (Original: (c ?? string.Empty).Trim(), Core: NameCore(c)))
            .Where(c => c.Core.Length > 0)
            .GroupBy(c => c.Original, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        if (prefix.Length == 0 || wanted.Count == 0)
        {
            return Array.Empty<SpellingSupport>();
        }

        using var connection = JavDb.OpenRead(JavDataPaths.TermsDb);
        if (connection == null || !JavDb.HasTable(connection, TableName))
        {
            return Array.Empty<SpellingSupport>();
        }

        var rows = new Dictionary<string, (int Rows, bool Pinned)>(StringComparer.Ordinal);
        foreach (var sql in new[]
                 {
                     "select ko, pinned from " + TableName + " where series = $series and quality = 'ok'",
                     "select ko, 0 as pinned from " + TableName + " where series = $series and quality = 'ok'",
                 })
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$series", prefix);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var core = NameCore(JavDb.GetText(reader, 0));
                    if (core.Length == 0)
                    {
                        continue;
                    }

                    var pinned = JavDb.GetBool(reader, 1);
                    foreach (var candidate in wanted)
                    {
                        if (!string.Equals(candidate.Core, core, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        rows.TryGetValue(candidate.Original, out var seen);
                        rows[candidate.Original] = (seen.Rows + 1, seen.Pinned || pinned);
                    }
                }

                break;
            }
            catch
            {
                // Older glossary without the pinned column - try the legacy shape, then give up.
                rows.Clear();
            }
        }

        return rows
            .Select(kvp => new SpellingSupport(kvp.Key, kvp.Value.Rows, kvp.Value.Pinned))
            .OrderByDescending(s => s.Pinned)
            .ThenByDescending(s => s.Rows)
            .ThenBy(s => s.Spelling, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A Korean address form with its trailing honorific removed, so the readings can be compared.</summary>
    internal static string NameCore(string? korean)
    {
        var text = (korean ?? string.Empty).Trim();
        // ★Longest first: 선생님 ends with 님, and stripping 님 from it would leave 선생 and make
        //   "타키모토 선생님" and "타키모토 선생" look like different readings.
        foreach (var honorific in KoreanHonorifics.OrderByDescending(h => h.Length))
        {
            if (text.Length > honorific.Length && text.EndsWith(honorific, StringComparison.Ordinal))
            {
                return text[..^honorific.Length].TrimEnd();
            }
        }

        return text;
    }

    internal static bool IsAddressForm(string source, string korean)
    {
        if (source.Length < 3 || korean.Length < 3)
        {
            return false;
        }

        // A source that already went through a machine translation is not a spelling to copy -
        // this is the same gate SpeakerContext applies to cast names.
        foreach (var c in source)
        {
            if (c is >= '가' and <= '힣')
            {
                return false;
            }
        }

        return EndsWithAny(source, JapaneseHonorifics) || EndsWithAny(korean, KoreanHonorifics);
    }

    private static readonly string[] JapaneseHonorifics =
        ["さん", "ちゃん", "君", "くん", "様", "先生", "サン", "チャン", "セン"];

    // ★No 상: it is a suffix of ordinary words (이상, 정상, 사상), and the Japanese side already
    //   catches the さん it would have covered.
    private static readonly string[] KoreanHonorifics =
        ["씨", "짱", "군", "님", "선생님"];

    private static bool EndsWithAny(string value, string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
