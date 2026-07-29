using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nikse.SubtitleEdit.Logic.Media;

/// <summary>
/// Metadata embedded in the video file itself, read straight out of the MP4 atoms.
///
/// ★Fork addition. It exists because the subtitle a user opens is almost always paired with a
///   video (<see cref="FindVideoFileName"/> resolves it), and that video carries the one thing a
///   Korean subtitle can no longer tell us: <b>who is speaking to whom</b>. The original-language
///   synopsis in <c>©cmt</c> names the relationships outright ("兄嫁である義姉", "一人息子の修一"),
///   and <c>©gen</c> compresses the setting into tags ("女教師", "人妻・主婦"). That is exactly the
///   input the speech-level (화계) pass has to be given by hand today.
///
/// ★Read with TagLib#, which is <b>already</b> a dependency (UI.csproj) - no ffmpeg/ffprobe process,
///   no new package. TagLib also reaches the two things the ffmpeg log makes awkward: the custom
///   <c>----:com.javtag:*</c> freeform atoms and <c>©dir</c>.
///
/// ★Everything here is fail-soft: an unreadable, tagless or non-media file yields
///   <see cref="Empty"/>, never an exception. A missing tag is a blank string, never null.
/// </summary>
public sealed class VideoTagInfo
{
    public static readonly VideoTagInfo Empty = new();

    /// <summary>Title as tagged (<c>©nam</c>). Usually the original Japanese title.</summary>
    public string Title { get; private init; } = string.Empty;

    /// <summary>Original-language title from the <c>com.javtag</c> freeform atom, when present.</summary>
    public string TitleOriginal { get; private init; } = string.Empty;

    /// <summary>Cast (<c>©ART</c>), already split on commas. May include non-performers.</summary>
    public IReadOnlyList<string> Performers { get; private init; } = Array.Empty<string>();

    /// <summary>Album (<c>©alb</c>). Carries the catalogue code on most files, a series name on some.</summary>
    public string Album { get; private init; } = string.Empty;

    /// <summary>Label / studio (<c>aART</c>).</summary>
    public string AlbumArtist { get; private init; } = string.Empty;

    /// <summary>Genre tags (<c>©gen</c>), already split on commas.</summary>
    public IReadOnlyList<string> Genres { get; private init; } = Array.Empty<string>();

    /// <summary>Release date as tagged (<c>©day</c>) - "2024-06-05" or just "2023".</summary>
    public string Date { get; private init; } = string.Empty;

    /// <summary>
    /// Comment (<c>©cmt</c>). ★On the dominant tagger this is the <b>original-language synopsis</b>
    /// and is the single most useful field here; on others it is just the catalogue code, and on
    /// one generation it is absent. Never assume - see <see cref="LooksLikeSynopsis"/>.
    /// </summary>
    public string Comment { get; private init; } = string.Empty;

    /// <summary>
    /// Description (<c>desc</c>). Usually a machine translation of <see cref="Comment"/>.
    /// ★Display only - feeding an MT'd synopsis to a model is strictly worse than the original.
    /// </summary>
    public string Description { get; private init; } = string.Empty;

    /// <summary>Director (<c>©dir</c>).</summary>
    public string Director { get; private init; } = string.Empty;

    public string Copyright { get; private init; } = string.Empty;

    /// <summary>Source page URL from the <c>com.javtag</c> freeform atom, when present.</summary>
    public string Url { get; private init; } = string.Empty;

    /// <summary>Writer of the tags (<c>©too</c>). Generations disagree on what a field means.</summary>
    public string Encoder { get; private init; } = string.Empty;

    public bool HasCoverArt { get; private init; }

    /// <summary>True when no tag worth showing was found.</summary>
    public bool IsEmpty =>
        Title.Length == 0 && Album.Length == 0 && Comment.Length == 0 &&
        Description.Length == 0 && Performers.Count == 0 && Genres.Count == 0;

    /// <summary>
    /// True when <see cref="Comment"/> reads like prose rather than a catalogue code.
    /// </summary>
    public bool LooksLikeSynopsis => IsSynopsis(Comment);

    /// <summary>
    /// ★A length test, not a language test. Measured over the library: where <c>©cmt</c> is prose
    ///   it runs 150-240 characters, and where it is a catalogue code it runs 8-12 ("ABF-027"),
    ///   so the two never come close to meeting. A language or pattern test would additionally
    ///   have to be right about Japanese, Korean and romanised codes; this does not.
    /// </summary>
    internal const int SynopsisMinimumLength = 40;

    internal static bool IsSynopsis(string? comment)
        => (comment?.Trim().Length ?? 0) >= SynopsisMinimumLength;

    /// <summary>
    /// The original-language synopsis, or an empty string when this file does not carry one.
    /// ★Deliberately never falls back to <see cref="Description"/>: that field is a machine
    ///   translation, and a bad translation of the relationships is worse than no relationships.
    /// </summary>
    public string OriginalSynopsis => LooksLikeSynopsis ? Comment : string.Empty;

    private VideoTagInfo()
    {
    }

    /// <summary>
    /// Reads the tags of <paramref name="fileName"/>. Returns <see cref="Empty"/> for anything that
    /// is not a readable media file - this is called on whatever the user opened, so it must never
    /// throw. ★Costs 120-170 ms on a multi-gigabyte file, so keep it off the UI thread.
    /// </summary>
    public static VideoTagInfo Read(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
        {
            return Empty;
        }

        try
        {
            using var file = TagLib.File.Create(fileName);
            var tag = file.Tag;
            var apple = file.GetTag(TagLib.TagTypes.Apple) as TagLib.Mpeg4.AppleTag;

            return new VideoTagInfo
            {
                Title = Clean(tag.Title),
                TitleOriginal = Dash(apple, "titleJa"),
                Performers = SplitList(tag.Performers),
                Album = Clean(tag.Album),
                AlbumArtist = Clean(FirstOrEmpty(tag.AlbumArtists)),
                Genres = SplitList(tag.Genres),
                Date = ReadDate(apple, tag),
                Comment = Clean(tag.Comment),
                Description = Clean(tag.Description),
                Director = Clean(FirstOrEmpty(Text(apple, AtomDirector))),
                Copyright = Clean(tag.Copyright),
                Url = Dash(apple, "url"),
                Encoder = Clean(FirstOrEmpty(Text(apple, AtomEncoder))),
                HasCoverArt = tag.Pictures is { Length: > 0 },
            };
        }
        catch
        {
            // Unsupported format, locked file, corrupt header - all mean "no tags", not an error.
            return Empty;
        }
    }

    /// <summary>
    /// ★<c>©day</c> holds "2024-06-05" but TagLib's <c>Year</c> narrows it to an int, losing the
    ///   month and day. Read the atom directly and keep <c>Year</c> only as the fallback.
    /// </summary>
    private static string ReadDate(TagLib.Mpeg4.AppleTag? apple, TagLib.Tag tag)
    {
        var raw = Clean(FirstOrEmpty(Text(apple, AtomDate)));
        if (raw.Length > 0)
        {
            return raw;
        }

        return tag.Year > 0 ? tag.Year.ToString() : string.Empty;
    }

    // ★These must be byte arrays, not strings. The atom names start with the single byte 0xA9,
    //   which is "©" in Latin-1 - but ByteVector(string) encodes as UTF-8, where "©" is two bytes
    //   (0xC2 0xA9). That silently yields a five-byte name that matches nothing, so every one of
    //   these fields would come back empty with no error to explain why.
    private static readonly TagLib.ByteVector AtomDirector = new(new byte[] { 0xA9, (byte)'d', (byte)'i', (byte)'r' });
    private static readonly TagLib.ByteVector AtomDate = new(new byte[] { 0xA9, (byte)'d', (byte)'a', (byte)'y' });
    private static readonly TagLib.ByteVector AtomEncoder = new(new byte[] { 0xA9, (byte)'t', (byte)'o', (byte)'o' });

    private static string[] Text(TagLib.Mpeg4.AppleTag? apple, TagLib.ByteVector atomName)
    {
        if (apple == null)
        {
            return Array.Empty<string>();
        }

        try
        {
            return apple.GetText(atomName) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string Dash(TagLib.Mpeg4.AppleTag? apple, string name)
    {
        if (apple == null)
        {
            return string.Empty;
        }

        try
        {
            return Clean(apple.GetDashBox("com.javtag", name));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstOrEmpty(string[]? values)
        => values is { Length: > 0 } ? values[0] : string.Empty;

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>
    /// ★MP4 stores the whole cast in one <c>©ART</c> string, so TagLib hands back a single-element
    ///   array holding "A, B, C" rather than three performers. Split here, and tolerate a tagger
    ///   that did use several atoms.
    ///
    /// ★Some taggers serialise a JSON array into the tag instead:
    ///   <c>["모모타 미츠키","근육 사와노","TECH"]</c> is a real value from this library. Splitting
    ///   that on commas leaves every entry wearing a quote and the last one a bracket, so a
    ///   downstream filter that drops some entries can leave <c>"TECH"]</c> standing on its own.
    ///   Parse it as JSON first, and scrub the punctuation off whatever the comma path produces so
    ///   a malformed array cannot leak debris either.
    /// </summary>
    internal static IReadOnlyList<string> SplitList(string[]? values)
    {
        if (values == null || values.Length == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var part in SplitOne(value))
            {
                var trimmed = Scrub(part);
                if (trimmed.Length > 0 && !result.Contains(trimmed, StringComparer.Ordinal))
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> SplitOne(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 1 && trimmed[0] == '[')
        {
            List<string>? parsed = null;
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    parsed = new List<string>();
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String)
                        {
                            parsed.Add(element.GetString() ?? string.Empty);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not valid JSON after all - the comma path below still has to cope.
            }

            if (parsed != null)
            {
                return parsed;
            }
        }

        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Removes the punctuation a broken JSON array leaves behind. No real name starts or
    /// ends with one of these.</summary>
    private static string Scrub(string part) => part.Trim().Trim('"', '[', ']', '\'').Trim();
}
