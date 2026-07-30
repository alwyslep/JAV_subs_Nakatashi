using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Tools.NameCheck;
using System.Text;

namespace UITests.Features.Tools;

/// <summary>
/// Fork addition. Lining the translation up with the film's original language is what lets a name
/// fix be remembered - and picking the wrong file would feed the shared glossary a line from another
/// cut of the film. These cover the refusals more than the successes, because the refusals are the
/// part that protects the data.
/// </summary>
public class OriginalDialogueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "original-dialogue-" + Guid.NewGuid().ToString("N"));

    public OriginalDialogueTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a test failure.
        }
    }

    private string Video(string stem = "NSFS-046 (1080p_aac)")
    {
        var path = Path.Combine(_dir, stem + ".mp4");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    /// <summary>Writes an SRT whose cue n starts at <paramref name="startsMs"/>[n].</summary>
    private string Srt(string name, IEnumerable<(int StartMs, string Text)> cues)
    {
        var sb = new StringBuilder();
        var number = 1;
        foreach (var (startMs, text) in cues)
        {
            sb.Append(number++).Append('\n')
              .Append(Stamp(startMs)).Append(" --> ").Append(Stamp(startMs + 900)).Append('\n')
              .Append(text).Append("\n\n");
        }

        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static string Stamp(int ms)
        => $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00},{ms % 1000:000}";

    private static Subtitle Korean(params (int StartMs, string Text)[] cues)
    {
        var subtitle = new Subtitle();
        foreach (var (startMs, text) in cues)
        {
            subtitle.Paragraphs.Add(new Paragraph(text, startMs, startMs + 900));
        }

        return subtitle;
    }

    private static (int, string)[] Pairs(int count, int step, string prefix)
    {
        var list = new (int, string)[count];
        for (var i = 0; i < count; i++)
        {
            list[i] = (i * step, prefix + i);
        }

        return list;
    }

    [Fact]
    public void For_LinesUpTheOriginalWhenTheTimecodesMatch()
    {
        var video = Video();
        Srt("NSFS-046 (1080p_aac).ja.srt", Pairs(40, 5000, "げんぶん"));
        var korean = Korean(Pairs(40, 5000, "번역"));

        var original = OriginalDialogue.For(korean, video, "ko");

        Assert.NotNull(original);
        Assert.Equal(1.0, original!.MatchRate, 3);
        Assert.Equal("げんぶん7", original.TextAt(korean.Paragraphs[7]));
    }

    // ★A subtitle does not record which file it was translated from, so a wrong original can be
    //   picked. Measured: real sources match 61-100% of cues, wrong ones 1-11%. Below the gate the
    //   file is discarded whole - pulling a line out of another cut and calling it the original
    //   would be worse than having no original at all.
    [Fact]
    public void For_RefusesAFileThatDoesNotLineUp()
    {
        var video = Video();
        Srt("NSFS-046 (1080p_aac).ja.srt", Pairs(40, 5000, "げんぶん"));
        // Same count, every cue shifted far past the tolerance.
        var korean = Korean(Pairs(40, 5000, "번역").Select(c => (c.Item1 + 3000, c.Item2)).ToArray());

        Assert.Null(OriginalDialogue.For(korean, video, "ko"));
    }

    // ★Widening the window was measured and gains nothing: the median match rate is 98% at 100 ms and
    //   still 98% at 2 s. So a near miss is a different line, not the same one recorded loosely.
    [Fact]
    public void TextAt_IgnoresACueOutsideTheTolerance()
    {
        var video = Video();
        var cues = Pairs(40, 5000, "げんぶん").ToList();
        cues[7] = (cues[7].Item1 + 400, cues[7].Item2);   // 400 ms out - four times the tolerance
        Srt("NSFS-046 (1080p_aac).ja.srt", cues);
        var korean = Korean(Pairs(40, 5000, "번역"));

        var original = OriginalDialogue.For(korean, video, "ko");

        Assert.NotNull(original);
        Assert.Equal(string.Empty, original!.TextAt(korean.Paragraphs[7]));
        Assert.Equal("げんぶん8", original.TextAt(korean.Paragraphs[8]));
    }

    [Fact]
    public void TextAt_AcceptsACueInsideTheTolerance()
    {
        var video = Video();
        var cues = Pairs(40, 5000, "げんぶん").ToList();
        cues[7] = (cues[7].Item1 + 80, cues[7].Item2);    // 80 ms out - inside 100 ms
        Srt("NSFS-046 (1080p_aac).ja.srt", cues);
        var korean = Korean(Pairs(40, 5000, "번역"));

        Assert.Equal("げんぶん7", OriginalDialogue.For(korean, video, "ko")!.TextAt(korean.Paragraphs[7]));
    }

    // ★Measured: 218 of 230 films with more than one original-language file had overlapping time
    //   spans - alternative rips, not parts. So one is chosen, and the plain name wins because in the
    //   cases that really were split (P1/P2) a full-length copy carried exactly that name.
    [Fact]
    public void Pick_PrefersThePlainlyNamedFile()
    {
        var video = Video();
        Srt("NSFS-046 (1080p_aac).ja.srt", Pairs(40, 5000, "せいほん"));
        Srt("NSFS-046 (1080p_aac).hhd800.com@NSFS-046-A-jp.ja.srt", Pairs(60, 5000, "べつばん"));
        var korean = Korean(Pairs(40, 5000, "번역"));

        var original = OriginalDialogue.For(korean, video, "ko");

        Assert.NotNull(original);
        Assert.EndsWith(".ja.srt", original!.FileName);
        Assert.Equal("せいほん7", original.TextAt(korean.Paragraphs[7]));
    }

    // Without a plain name, the longest file wins - measured, that is the full-length copy (858 cues
    // beat 550 and 65).
    [Fact]
    public void Pick_FallsBackToTheFileWithTheMostCues()
    {
        var video = Video();
        Srt("NSFS-046 (1080p_aac).AVOP 165 P2.ja.srt", Pairs(8, 5000, "かけら"));
        Srt("NSFS-046 (1080p_aac).whisperjav.ja.srt", Pairs(40, 5000, "ぜんたい"));
        var korean = Korean(Pairs(40, 5000, "번역"));

        Assert.Equal("ぜんたい7", OriginalDialogue.For(korean, video, "ko")!.TextAt(korean.Paragraphs[7]));
    }

    [Fact]
    public void For_SkipsTheLanguageBeingChecked()
    {
        var video = Video();
        Srt("NSFS-046 (1080p_aac).ja.srt", Pairs(40, 5000, "げんぶん"));
        var korean = Korean(Pairs(40, 5000, "번역"));

        // Checking the Japanese subtitle itself must not read the Japanese subtitle as its original.
        Assert.Null(OriginalDialogue.For(korean, video, "ja"));
    }

    [Theory]
    [InlineData("a.ja.srt", true)]
    [InlineData("a.JA.SRT", true)]
    [InlineData("a.ja.ass", true)]
    [InlineData("a.ko.srt", false)]
    [InlineData("a.jaa.srt", false)]
    [InlineData("a.ja.txt", false)]
    [InlineData("ja.srt", false)]
    public void IsOriginalLanguageSubtitle_MatchesTheCodeRightBeforeTheExtension(string name, bool expected)
    {
        Assert.Equal(expected, OriginalDialogue.IsOriginalLanguageSubtitle(Path.Combine(_dir, name), "ja"));
    }

    // ★Measured over 400 files named *.ja.srt: 257 Japanese, 124 Chinese, 16 romanised, 3 Korean.
    //   A live run read 希米卡 out of one of them and wrote it over the correct 由美香, which is what
    //   this test is here to keep fixed. Names like JUL-224.zh.ja.srt really exist.
    [Fact]
    public void For_RefusesAFileNamedJapaneseThatIsActuallyChinese()
    {
        var video = Video();
        Srt("NSFS-046 (1080p_aac).zh-tw-繁中.ja.srt",
            Pairs(40, 5000, string.Empty).Select(c => (c.Item1, "我是井上很多年了")).ToArray());
        var korean = Korean(Pairs(40, 5000, "번역"));

        Assert.Null(OriginalDialogue.For(korean, video, "ko"));
    }

    [Fact]
    public void For_AcceptsRealJapaneseAlongsideAChineseImpostor()
    {
        var video = Video();
        Srt("NSFS-046 (1080p_aac).aaa-zh.ja.srt",
            Pairs(40, 5000, string.Empty).Select(c => (c.Item1, "我是井上很多年了")).ToArray());
        Srt("NSFS-046 (1080p_aac).bbb.ja.srt",
            Pairs(40, 5000, string.Empty).Select(c => (c.Item1, "ほら、宅本さん、行ってください")).ToArray());
        var korean = Korean(Pairs(40, 5000, "번역"));

        // The Chinese file has more cues in neither direction here; what decides it is the content
        // test, and Pick's choice must not be able to shadow that.
        var original = OriginalDialogue.For(korean, video, "ko");

        Assert.NotNull(original);
        Assert.Contains("宅本", original!.TextAt(korean.Paragraphs[3]));
    }

    [Theory]
    [InlineData("ほら、宅本さん、1つまま行ってやってください", true)]
    [InlineData("我是井上 跟老公结婚很多年了", false)]
    [InlineData("Hello there, Mr Takimoto", false)]
    [InlineData("여보, 다녀왔어요", false)]
    [InlineData("", false)]
    public void HasEnoughKana_SeparatesJapaneseFromTheOthers(string text, bool expected)
    {
        Assert.Equal(expected, OriginalDialogue.HasEnoughKana([text]));
    }

    // ★A language with no content test must turn the feature off, not wave the file through - the file
    //   name is exactly what cannot be trusted.
    [Fact]
    public void LooksLikeLanguage_RefusesALanguageItCannotTest()
    {
        Assert.False(OriginalDialogue.LooksLikeLanguage("zh", ["我是井上"]));
        Assert.False(OriginalDialogue.LooksLikeLanguage("en", ["Hello there"]));
        Assert.True(OriginalDialogue.LooksLikeLanguage("ja", ["ほら、宅本さん、行ってください"]));
    }

    // ★Fail-soft is not decoration: measured, 5 of 749 films carry an original-language file whose
    //   timecodes cannot be read at all.
    [Fact]
    public void For_ReturnsNullOnAnUnreadableFile()
    {
        var video = Video();
        File.WriteAllText(Path.Combine(_dir, "NSFS-046 (1080p_aac).ja.srt"), "not a subtitle at all");

        Assert.Null(OriginalDialogue.For(Korean(Pairs(40, 5000, "번역")), video, "ko"));
    }

    [Fact]
    public void For_ReturnsNullWithNoVideoAndNoFolder()
    {
        var korean = Korean(Pairs(40, 5000, "번역"));

        Assert.Null(OriginalDialogue.For(korean, null, "ko"));
        Assert.Null(OriginalDialogue.For(korean, string.Empty, "ko"));
        Assert.Null(OriginalDialogue.For(korean, Path.Combine(_dir, "no-such-folder", "x.mp4"), "ko"));
        Assert.Null(OriginalDialogue.For(new Subtitle(), Video(), "ko"));
    }
}
