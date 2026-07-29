using Nikse.SubtitleEdit.Logic.JavData;

namespace UITests.Logic;

/// <summary>
/// Fork addition. Covers the parts of <see cref="JavCatalog"/> that decide without the database;
/// the lookup itself is verified against the real catalogue.
/// </summary>
public class JavCatalogTests
{
    // Real file names from the library.
    [Theory]
    [InlineData(@"F:\18R\ABF-108 (1080p_aac).mp4", "ABF-108")]
    [InlineData(@"F:\18R_V\ROE-476 (1080p_aac).mp4", "ROE-476")]
    [InlineData(@"F:\18R\RED-060 (632p_aac).mp4", "RED-060")]
    [InlineData(@"H:\10musume\musume-010117-01 (720p_aac).mp4", "musume-010117-01")]
    // ★Some files are wrapped in underscores outside the quality group, so both ends have to be
    //   trimmed after the group is removed as well as before.
    [InlineData(@"G:\1pondo\_caribbeancom-020219-852 (720p_aac)_.mp4", "caribbeancom-020219-852")]
    [InlineData(@"X:\somewhere\FSDSS-424.mp4", "FSDSS-424")]
    [InlineData(@"X:\a\Korean Wife Cheating at Motel- In Japanese Porn (480p_aac).mp4",
        "Korean Wife Cheating at Motel- In Japanese Porn")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void CandidateFromFileName_StripsTheQualityGroupAndStrayUnderscores(string? path, string expected)
    {
        Assert.Equal(expected, JavCatalog.CandidateFromFileName(path));
    }

    // Only reached when the catalogue cannot answer, so it must reject prose rather than guess.
    [Theory]
    [InlineData("ABF-108", true)]
    [InlineData("caribbeancom-020219-852", true)]
    [InlineData("H4610-KI190615", true)]
    [InlineData("fc2-ppv-4620659", true)]
    [InlineData("pondo-022718_651", true)]
    [InlineData("Korean Wife Cheating at Motel", false)]  // spaces
    [InlineData("ABF", false)]                            // no digits
    [InlineData("12345", false)]                          // no letters
    [InlineData("AB1", false)]                            // too short
    [InlineData("中出し 射精執行官", false)]                  // a series name, not a code
    [InlineData("", false)]
    public void LooksLikeCode_AcceptsCodesAndRejectsProse(string candidate, bool expected)
    {
        Assert.Equal(expected, JavCatalog.LooksLikeCode(candidate));
    }

    // The catalogue writes lists as JSON on newer rows and comma-joined text on older ones.
    [Fact]
    public void SplitStored_ReadsAJsonArray()
    {
        Assert.Equal(["早川夏美", "早瀬未来"], JavCatalog.SplitStored("[\"早川夏美\", \"早瀬未来\"]"));
    }

    [Fact]
    public void SplitStored_ReadsACommaJoinedString()
    {
        Assert.Equal(["大島優香"], JavCatalog.SplitStored("大島優香"));
        Assert.Equal(["A", "B"], JavCatalog.SplitStored("A, B"));
    }

    [Fact]
    public void SplitStored_OnJunkIsEmptyNotAnException()
    {
        Assert.Empty(JavCatalog.SplitStored(""));
        Assert.Empty(JavCatalog.SplitStored("   "));
    }

    // ★A row that starts like a JSON array but is not one still must not hand back its punctuation
    //   as part of a name - that debris is what put "TECH"] in front of a model once already.
    [Fact]
    public void SplitStored_ScrubsPunctuationOffAMalformedArray()
    {
        Assert.Equal(["not json"], JavCatalog.SplitStored("[not json"));
        Assert.Equal(["大島優香", "TECH"], JavCatalog.SplitStored("[\"大島優香\",\"TECH\""));
    }
}
