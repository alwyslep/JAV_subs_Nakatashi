using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.SpeechToText;

namespace UITests.Features.Video.SpeechToText;

/// <summary>
/// The post-processor's CJK branches are selected by language code, and three of them were testing
/// for codes this application never produces.
///
/// ★The codes that actually arrive come from <c>WhisperLanguage</c>'s own list - which declares
///   <c>ja</c>, <c>zh</c>, <c>yue</c> and no <c>jp</c> or <c>cn</c> at all - or from an online
///   engine's language hint, or from Google auto-detect. So <c>language == "jp"</c> and
///   <c>language == "cn"</c> were dead branches: Japanese and Chinese merged against the Latin
///   limit of 86 characters instead of their own 32 and 36, and Chinese additionally missed
///   <c>IsNonStandardLineTerminationLanguage</c> entirely, so full stops were appended to it and
///   its lines were split on whitespace that does not exist.
///
/// These tests exist so that stays fixed.
/// </summary>
public class PostProcessorLanguageCodeTests
{
    private static Subtitle TwoShortCues()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("あ", 1000, 2000));
        subtitle.Paragraphs.Add(new Paragraph("い", 2050, 3000));
        return subtitle;
    }

    /// <summary>The CJK merge cap must apply to the code the application actually uses.</summary>
    [Theory]
    [InlineData("ja", 32)]     // the real Japanese code - was 86 before this fix
    [InlineData("jp", 32)]     // kept: nothing here produces it, but a caller elsewhere might
    [InlineData("zh", 36)]     // the real Chinese code - was 86 before this fix
    [InlineData("cn", 36)]     // kept for the same reason as "jp"
    [InlineData("yue", 36)]
    public void MergeShortLines_AppliesTheCjkCharacterCapForTheCodesThisAppProduces(string language, int expected)
    {
        var processor = new SpeechToTextPostProcessor(language);
        processor.MergeShortLines(TwoShortCues(), language);

        Assert.Equal(expected, processor.ParagraphMaxChars);
    }

    /// <summary>A Latin language keeps the Latin cap - the fix must not widen to everything.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("da")]
    [InlineData("ko")]         // Korean DOES space its words; it is not a CJK case here
    public void MergeShortLines_LeavesTheLatinCapAloneForEverythingElse(string language)
    {
        var processor = new SpeechToTextPostProcessor(language);
        processor.MergeShortLines(TwoShortCues(), language);

        Assert.Equal(86, processor.ParagraphMaxChars);
    }

    /// <summary>
    /// ★The worst of the three. Chinese was not recognised as a language whose lines do not
    ///   terminate the Latin way, so a cue followed by a long gap had a "." appended to it.
    /// </summary>
    [Fact]
    public void AddPeriods_DoesNotAppendAFullStopToChinese()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("你好", 1000, 2000));
        subtitle.Paragraphs.Add(new Paragraph("再见", 5000, 6000));   // a 3 s gap: well past the threshold

        var result = new SpeechToTextPostProcessor("zh").AddPeriods(subtitle, "zh");

        Assert.Equal("你好", result.Paragraphs[0].Text);
        Assert.Equal("再见", result.Paragraphs[1].Text);
    }

    [Fact]
    public void AddPeriods_DoesNotAppendAFullStopToJapanese()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("そうですね", 1000, 2000));
        subtitle.Paragraphs.Add(new Paragraph("わかりました", 5000, 6000));

        var result = new SpeechToTextPostProcessor("ja").AddPeriods(subtitle, "ja");

        Assert.Equal("そうですね", result.Paragraphs[0].Text);
        Assert.Equal("わかりました", result.Paragraphs[1].Text);
    }

    /// <summary>...while English still gets its full stops, which is what the pass is for.</summary>
    [Fact]
    public void AddPeriods_StillWorksOnEnglish()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("hello there", 1000, 2000));
        subtitle.Paragraphs.Add(new Paragraph("goodbye", 5000, 6000));

        var result = new SpeechToTextPostProcessor("en").AddPeriods(subtitle, "en");

        Assert.Equal("hello there.", result.Paragraphs[0].Text);
        Assert.Equal("goodbye.", result.Paragraphs[1].Text);
    }
}
