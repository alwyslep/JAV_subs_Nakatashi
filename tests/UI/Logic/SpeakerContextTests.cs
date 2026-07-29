using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.JavData;

namespace UITests.Logic;

/// <summary>
/// Fork addition. Covers how the relationship note is assembled when nothing has been recorded for
/// a film; the four-source chain itself is verified against the real catalogues.
/// </summary>
public class SpeakerContextTests
{
    private static readonly string[] None = [];

    [Fact]
    public void Assemble_PutsEachFieldOnItsOwnLine()
    {
        var note = SpeakerContext.Assemble(
            "卒業式の後…厳しく指導してくれた女教師へ",
            ["人妻・主婦", "女教師"],
            ["大島優香"],
            "長く教職に従事する優香は、受け持つ生徒たちを卒業へ導くため。");

        Assert.Contains("title: 卒業式の後…厳しく指導してくれた女教師へ", note);
        Assert.Contains("genre: 人妻・主婦, 女教師", note);
        Assert.Contains("cast: 大島優香", note);
        Assert.Contains("synopsis: 長く教職に従事する優香は", note);
    }

    // ★The prompt introduces this block as "who speaks how in this film". For a guidebook that is
    //   true; for a plot summary it is not, and without saying so the model is being told that a
    //   synopsis is a set of speech-level rules.
    [Fact]
    public void Assemble_SaysOutrightThatNoRulesWereRecorded()
    {
        var note = SpeakerContext.Assemble("t", ["g"], None, "s");

        Assert.StartsWith("No per-speaker rules were recorded for this film.", note);
    }

    // Nothing to reason from - an empty box is more honest than a title on its own.
    [Fact]
    public void Assemble_NeedsMoreThanATitle()
    {
        Assert.Equal(string.Empty, SpeakerContext.Assemble("제목뿐", None, None, string.Empty));
        Assert.Equal(string.Empty, SpeakerContext.Assemble(string.Empty, None, None, string.Empty));
    }

    [Fact]
    public void Assemble_WorksFromGenreAlone()
    {
        // Genres alone carry real signal here - "女教師" says who is talking to whom.
        var note = SpeakerContext.Assemble(string.Empty, ["女教師"], None, string.Empty);

        Assert.Contains("genre: 女教師", note);
    }

    // ★A synopsis with hard line breaks would otherwise turn one labelled field into several
    //   unlabelled ones, and the block stops being readable as key/value.
    [Fact]
    public void Assemble_FlattensLineBreaksInsideAField()
    {
        var note = SpeakerContext.Assemble(string.Empty, None, None, "첫 줄\r\n둘째 줄\n셋째 줄");

        Assert.Contains("synopsis: 첫 줄 둘째 줄 셋째 줄", note);
        Assert.Equal(1, note.Split('\n').Length - 1);
    }

    [Fact]
    public void Assemble_IsCappedSoItCannotFloodEveryBatch()
    {
        var note = SpeakerContext.Assemble("t", None, None, new string('가', 5000));

        Assert.True(note.Length <= SpeakerContext.MaxAssembledLength, note.Length.ToString());
    }

    // ★The fork's hard rule about names, at the one place names enter a prompt: a name must be a
    //   transliteration, never a translation. Roughly a fifth of this library was tagged by a
    //   machine-translation pass that read the characters for their meaning.
    [Fact]
    public void KeepOriginalNames_DropsMachineTranslatedKoreanNames()
    {
        // 鈴の家りん -> "린의 집 인" (Rin's house person), 蝦原 -> "새우하라" (shrimp-hara).
        var kept = SpeakerContext.KeepOriginalNames(
            ["린의 집 인", "아베 토모히로", "大島優香", "早川夏美"]);

        Assert.Equal(["大島優香", "早川夏美"], kept);
    }

    [Fact]
    public void KeepOriginalNames_KeepsKanjiKanaAndRomaji()
    {
        var kept = SpeakerContext.KeepOriginalNames(["市川まさみ", "流川はる香", "Nomo", "ピエロ田"]);

        Assert.Equal(["市川まさみ", "流川はる香", "Nomo", "ピエロ田"], kept);
    }

    [Fact]
    public void KeepOriginalNames_OnAnAllKoreanCastIsEmpty()
    {
        // Emptying it is the point - the caller then falls back to the catalogue's own names.
        Assert.Empty(SpeakerContext.KeepOriginalNames(["린의 집 인", "타치바나"]));
        Assert.Empty(SpeakerContext.KeepOriginalNames(None));
    }

    // ★A real list from the library: ["모모타 미츠키", "근육 사와노", …, "TECH"]. Only "TECH" has no
    //   Hangul, and it is a studio credit - keeping it would present a studio as the cast.
    [Fact]
    public void TrustedNames_DiscardsTheWholeListWhenAnyNameWasMachineTranslated()
    {
        Assert.Empty(SpeakerContext.TrustedNames(["모모타 미츠키", "근육 사와노", "TECH"]));
    }

    [Fact]
    public void TrustedNames_KeepsAListThatLostNothing()
    {
        Assert.Equal(
            ["桜木優希音", "高山えみり", "せいの彩葉"],
            SpeakerContext.TrustedNames(["桜木優希音", "高山えみり", "せいの彩葉"]));
    }

    // ★An instruction, not evidence. A name the series has settled on must not be respelled by a
    //   pass that is only supposed to be changing sentence endings.
    [Fact]
    public void Assemble_AppendsTheSeriesNameSpellingsAsAnInstruction()
    {
        var note = SpeakerContext.Assemble(
            "t", None, None, "s",
            [new JavTermPair { Source = "佐々木さん", Korean = "사사키 씨" },
             new JavTermPair { Source = "亮くん", Korean = "료 군" }]);

        Assert.Contains("keep these spellings exactly: 佐々木さん = 사사키 씨, 亮くん = 료 군", note);
    }

    [Fact]
    public void Assemble_WorksFromNameSpellingsAlone()
    {
        var note = SpeakerContext.Assemble(
            string.Empty, None, None, string.Empty,
            [new JavTermPair { Source = "佐々木さん", Korean = "사사키 씨" }]);

        Assert.Contains("佐々木さん = 사사키 씨", note);
    }

    [Fact]
    public void Resolve_WithoutAVideoIsEmpty()
    {
        Assert.True(SpeakerContext.Resolve(null).IsEmpty);
        Assert.True(SpeakerContext.Resolve("   ").IsEmpty);
        Assert.Equal(SpeakerContextSource.None, SpeakerContext.Resolve(null).Source);
    }

    // The catalogues live on another drive that may not be mounted; that is a normal state.
    [Fact]
    public void Resolve_WithNoCataloguesReachableIsEmptyNotAnError()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData { DataFolder = @"Z:\nothing-here" };

            var result = SpeakerContext.Resolve(@"Z:\nothing-here\ABF-108 (1080p_aac).mp4");

            Assert.True(result.IsEmpty);
            Assert.False(result.IsHumanWritten);
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }
}
