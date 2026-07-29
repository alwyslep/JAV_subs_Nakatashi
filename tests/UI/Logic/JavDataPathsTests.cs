using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.JavData;

namespace UITests.Logic;

/// <summary>
/// Fork addition.
///
/// ★The normalisation cases below are the contract with the sibling translator project, whose
///   <c>guidebook.py::_key()</c> must produce byte-identical output: non <c>[A-Za-z0-9._-]</c>
///   becomes <c>_</c>, leading dots are stripped, capped at 80. The two programs look each other's
///   rows up by this string, and a mismatch does not raise - it silently finds nothing. The values
///   here were taken from that project's own self-check so a drift on either side fails a test.
/// </summary>
public class JavDataPathsTests
{
    [Theory]
    [InlineData("HND-077", "HND-077")]
    [InlineData("  HND-077  ", "HND-077")]
    [InlineData("abf-108", "abf-108")]
    [InlineData("ABF 108", "ABF_108")]
    [InlineData("MIDE_900.v2", "MIDE_900.v2")]
    // The translator's own test case, and its exact expected key.
    [InlineData("../../evil AB-1", "_.._evil_AB-1")]
    [InlineData("...leading", "leading")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeCode_MatchesTheTranslatorsKeyRule(string? code, string expected)
    {
        Assert.Equal(expected, JavDataPaths.NormalizeCode(code));
    }

    [Fact]
    public void NormalizeCode_CapsAtEightyCharacters()
    {
        Assert.Equal(80, JavDataPaths.NormalizeCode(new string('A', 200)).Length);
    }

    // The glossary's series layer is keyed by the prefix; the global layer by an empty string.
    [Theory]
    [InlineData("ABF-108", "ABF")]
    [InlineData("musume-010117-01", "musume")]
    // ★No separator means no series - returning the whole code would collide with a real prefix,
    //   and returning it blank must not be mistaken for the global layer by the caller.
    [InlineData("NOSEPARATOR", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SeriesPrefix_TakesWhatIsBeforeTheFirstDash(string? code, string expected)
    {
        Assert.Equal(expected, JavDataPaths.SeriesPrefix(code));
    }

    [Fact]
    public void Paths_DefaultToTheTranslatorsOwnLocation()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData();

            Assert.Equal(Path.Combine(SeJavData.DefaultDataFolder, SeJavData.TermsDbFileName), JavDataPaths.TermsDb);
            Assert.Equal(Path.Combine(SeJavData.DefaultDataFolder, SeJavData.RegisterDbFileName), JavDataPaths.RegisterDb);
            Assert.Equal(Path.Combine(SeJavData.DefaultDataFolder, SeJavData.CatalogDbFileName), JavDataPaths.CatalogDb);
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }

    [Fact]
    public void Paths_FollowTheConfiguredFolder()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData { DataFolder = @"E:\shared" };

            Assert.Equal(Path.Combine(@"E:\shared", SeJavData.TermsDbFileName), JavDataPaths.TermsDb);
            Assert.Equal(Path.Combine(@"E:\shared", SeJavData.RegisterDbFileName), JavDataPaths.RegisterDb);
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }

    // ★An explicit path is honoured verbatim even when it does not exist. Falling back to the
    //   default would turn a typo into "the feature quietly reads the wrong database".
    [Fact]
    public void Paths_LetAnExplicitFileOverrideTheFolder()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData
            {
                DataFolder = @"E:\shared",
                TermsDbPath = @"Z:\elsewhere\terms.sqlite",
            };

            Assert.Equal(@"Z:\elsewhere\terms.sqlite", JavDataPaths.TermsDb);
            Assert.Equal(Path.Combine(@"E:\shared", SeJavData.RegisterDbFileName), JavDataPaths.RegisterDb);
            Assert.False(JavDataPaths.HasTermsDb);
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }

    [Fact]
    public void Writing_IsRefusedWhenTheDatabaseIsNotThere()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData { DataFolder = @"Z:\nothing-here", AllowWrite = true };

            Assert.False(JavDataPaths.CanWriteRegisterDb);
            Assert.False(JavGuidebook.Save("HND-077", "이 저장은 조용히 실패해야 한다"));
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }

    [Fact]
    public void Guidebook_LoadOnAnUnreachableDatabaseIsNullNotAnError()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData { DataFolder = @"Z:\nothing-here" };

            Assert.Null(JavGuidebook.Load("HND-077"));
            Assert.Null(JavGuidebook.Load(null));
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }
}
