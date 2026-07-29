using Nikse.SubtitleEdit.Logic.Media;

namespace UITests.Logic;

/// <summary>
/// Fork addition. Covers the decisions <see cref="VideoTagInfo"/> makes on its own; the atom
/// reading itself needs a real MP4 and is verified against the library instead.
/// </summary>
public class VideoTagInfoTests
{
    // MP4 keeps the whole cast in one atom, so TagLib hands back ["A, B, C"], not three entries.
    [Fact]
    public void SplitList_SplitsOneCommaJoinedAtomIntoPerformers()
    {
        var result = VideoTagInfo.SplitList(["早川夏美, 早瀬未来"]);

        Assert.Equal(["早川夏美", "早瀬未来"], result);
    }

    // A tagger that did use several atoms must work too.
    [Fact]
    public void SplitList_FlattensSeveralAtoms()
    {
        var result = VideoTagInfo.SplitList(["人妻・主婦", "女教師, 熟女"]);

        Assert.Equal(["人妻・主婦", "女教師", "熟女"], result);
    }

    [Fact]
    public void SplitList_DropsBlanksAndDuplicates()
    {
        var result = VideoTagInfo.SplitList(["巨乳, , 巨乳,  単体作品 ", "", null!]);

        Assert.Equal(["巨乳", "単体作品"], result);
    }

    [Fact]
    public void SplitList_OnNothingIsEmptyNotNull()
    {
        Assert.Empty(VideoTagInfo.SplitList(null));
        Assert.Empty(VideoTagInfo.SplitList([]));
    }

    // ★A real ©ART from the library: one tagger serialises a JSON array into the tag. Splitting it
    //   on commas leaves every entry wearing a quote and the last one a bracket.
    [Fact]
    public void SplitList_ParsesAJsonArrayWrittenIntoTheTag()
    {
        var result = VideoTagInfo.SplitList(["[\"모모타 미츠키\",\"근육 사와노\",\"TECH\"]"]);

        Assert.Equal(["모모타 미츠키", "근육 사와노", "TECH"], result);
    }

    // Even when the array is truncated the debris must not survive as a name.
    [Fact]
    public void SplitList_ScrubsDebrisFromAMalformedArray()
    {
        var result = VideoTagInfo.SplitList(["[\"모모타 미츠키\",\"TECH\""]);

        Assert.Equal(["모모타 미츠키", "TECH"], result);
    }

    [Fact]
    public void SplitList_LeavesOrdinaryNamesAlone()
    {
        Assert.Equal(["大島優香"], VideoTagInfo.SplitList(["大島優香"]));
    }

    // Read is called on whatever the user opened - a missing or non-media file is normal input.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\no\such\video-9f3a1c.mp4")]
    public void Read_OnUnusableInputReturnsEmptyAndDoesNotThrow(string? fileName)
    {
        var tags = VideoTagInfo.Read(fileName);

        Assert.True(tags.IsEmpty);
        Assert.Equal(string.Empty, tags.Title);
        Assert.Empty(tags.Performers);
    }

    [Fact]
    public void Read_OnANonMediaFileReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a video");
            Assert.True(VideoTagInfo.Read(path).IsEmpty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Empty_IsEmpty()
    {
        Assert.True(VideoTagInfo.Empty.IsEmpty);
        Assert.Equal(string.Empty, VideoTagInfo.Empty.OriginalSynopsis);
        Assert.False(VideoTagInfo.Empty.LooksLikeSynopsis);
    }

    // ©cmt means different things on different taggers. Real values from the library: a catalogue
    // code on one generation, a 150-240 character synopsis on another, absent on a third.
    [Theory]
    [InlineData("ABF-027", false)]                       // catalogue code
    [InlineData("SDNM-013", false)]
    [InlineData("", false)]                              // jav_tag_v2 writes no ©cmt at all
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("長く教職に従事する優香は、受け持つ生徒たちを卒業へ導くため、日々厳しい指導にあたっていた。", true)]
    [InlineData("「旦那が喜ぶなら」とAV出演を続ける人妻・相原さん。今作は、スワッピングで有名な某温泉を旦那さん自ら手配し", true)]
    public void IsSynopsis_TellsProseFromACatalogueCode(string? comment, bool expected)
    {
        Assert.Equal(expected, VideoTagInfo.IsSynopsis(comment));
    }
}
