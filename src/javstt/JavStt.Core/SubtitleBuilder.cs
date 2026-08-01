using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.OpenAiCompatible;

namespace JavStt.Core;

/// <summary>
/// Turns the transcriber's timed segments into a subtitle file.
///
/// ★Uses LibSE's own <see cref="Subtitle"/> and <see cref="SubRip"/> rather than formatting SRT by
///   hand, so the output is byte-identical to what Subtitle Edit writes - the file is going back
///   into that editor, and a hand-rolled writer would differ on the details that matter there
///   (BOM, line endings, numbering after a renumber).
/// </summary>
public static class SubtitleBuilder
{
    /// <summary>
    /// A cue shorter than this cannot be read. Fun-ASR returns a handful per film (86 of 1264 on
    /// the measured film); they are kept but stretched, because dropping a line loses dialogue and
    /// the timing is the part that is wrong.
    /// </summary>
    internal const double MinimumCueSeconds = 0.5;

    /// <summary>
    /// ★A cue is never stretched past the next one's start. Overlapping cues make the editor's grid
    ///   show two lines at once and are worse than a short cue.
    /// </summary>
    public static Subtitle Build(IReadOnlyList<OpenAiCompatibleSegment>? segments)
    {
        var subtitle = new Subtitle();
        if (segments == null || segments.Count == 0)
        {
            return subtitle;
        }

        var ordered = segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .OrderBy(s => s.Start)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var segment = ordered[i];
            var start = Math.Max(0, segment.Start);
            var end = Math.Max(start, segment.End);

            if (end - start < MinimumCueSeconds)
            {
                var ceiling = i + 1 < ordered.Count ? ordered[i + 1].Start : double.MaxValue;
                end = Math.Min(start + MinimumCueSeconds, Math.Max(start, ceiling));
            }

            subtitle.Paragraphs.Add(new Paragraph(
                segment.Text.Trim(),
                start * TimeCode.BaseUnit,
                end * TimeCode.BaseUnit));
        }

        subtitle.Renumber();
        return subtitle;
    }

    /// <summary>Writes SRT next to the video, e.g. <c>ABF-062.mp4</c> → <c>ABF-062.ja.srt</c>.</summary>
    public static string Save(Subtitle subtitle, string videoPath, string languageSuffix)
    {
        var path = OutputPathFor(videoPath, languageSuffix);
        File.WriteAllText(path, subtitle.ToText(new SubRip()), System.Text.Encoding.UTF8);
        return path;
    }

    public static string OutputPathFor(string videoPath, string languageSuffix)
    {
        var directory = Path.GetDirectoryName(videoPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(videoPath);
        var suffix = string.IsNullOrWhiteSpace(languageSuffix) ? string.Empty : "." + languageSuffix.Trim('.');
        return Path.Combine(directory, stem + suffix + ".srt");
    }
}
