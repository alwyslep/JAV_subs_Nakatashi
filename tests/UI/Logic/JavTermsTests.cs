using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.JavData;

namespace UITests.Logic;

/// <summary>
/// Fork addition. Covers which glossary entries count as an address form. The values below are real
/// rows from the shared glossary, including the ones that made the length floors necessary.
/// </summary>
public class JavTermsTests
{
    // Names a character is addressed by - what the glossary actually holds, and what a speech-level
    // pass must not respell.
    [Theory]
    [InlineData("佐々木さん", "사사키 씨")]
    [InlineData("西村先生", "니시무라 선생님")]
    [InlineData("伊藤君", "이토 군")]
    [InlineData("加藤くん", "가토 군")]
    [InlineData("かんなちゃん", "칸나짱")]
    [InlineData("Mizunomi-chan", "미즈노미 짱")]
    [InlineData("下町がさん", "시모마치 씨")]
    public void IsAddressForm_AcceptsAnAddressedName(string source, string korean)
    {
        Assert.True(JavTerms.IsAddressForm(source, korean));
    }

    // ★Every one of these is a real row that the honorific rule alone would have accepted.
    [Theory]
    [InlineData("君", "너")]            // the bare honorific matching itself
    [InlineData("王様", "왕님")]         // a title, not a person
    [InlineData("叔叔", "아저씨")]        // a relation word, not a name
    [InlineData("変", "이상")]           // ordinary Korean ending in 상
    [InlineData("正常", "정상")]
    [InlineData("食べて下さい", "빨아 주세요")]  // ordinary dialogue
    [InlineData("立ってよ", "일어나 봐")]
    public void IsAddressForm_RejectsWhatIsNotAnAddressedName(string source, string korean)
    {
        Assert.False(JavTerms.IsAddressForm(source, korean));
    }

    // ★The same gate as the cast list: a source that has already been machine-translated into
    //   Korean is not a spelling worth copying.
    [Fact]
    public void IsAddressForm_RejectsAKoreanPollutedSource()
    {
        Assert.False(JavTerms.IsAddressForm("토아 코토네", "토아 코토네"));
        Assert.False(JavTerms.IsAddressForm("린의 집 인씨", "린의 집 인 씨"));
    }

    [Fact]
    public void AddressForms_WithoutASeriesIsEmpty()
    {
        Assert.Empty(JavTerms.AddressForms(null));
        Assert.Empty(JavTerms.AddressForms("   "));
    }

    [Fact]
    public void AddressForms_WithNoGlossaryReachableIsEmptyNotAnError()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData { DataFolder = @"Z:\nothing-here" };
            Assert.Empty(JavTerms.AddressForms("NSFS"));
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }

    // ★The rule, at the one place a spelling enters the shared glossary: a source that already
    //   contains Hangul went through the machine-translation pass, so pinning it would carve a
    //   mistranslation into data the translator will then trust. Same gate as termdb.pin_term().
    [Theory]
    [InlineData("HND", "린의 집 인", "스즈노야 린")]   // source already machine-translated
    [InlineData("HND", "", "사사키 씨")]
    [InlineData("HND", "佐々木さん", "   ")]
    [InlineData("", "佐々木さん", "사사키 씨")]
    [InlineData(null, "佐々木さん", "사사키 씨")]
    public void Pin_RefusesWhatMustNotEnterTheGlossary(string? series, string? source, string? korean)
    {
        Assert.False(JavTerms.Pin(series, source, korean));
    }

    /// <summary>
    /// ★Microsoft.Data.Sqlite pools connections, so the file stays open after the last one is
    ///   disposed and the folder cannot be removed. Clearing the pool is the documented way out; a
    ///   leftover temp folder is still not worth failing a test over.
    /// </summary>
    private static void Cleanup(string folder)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Nothing to do about it, and nothing broken by it.
        }
    }

    /// <summary>A glossary with just enough of the real shape to answer these questions.</summary>
    private static string MakeGlossary(string folder, params (string Series, string Src, string Ko, int Pinned)[] rows)
    {
        var path = Path.Combine(folder, "jav-terms.sqlite");
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path);
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText =
                "create table terms(id integer primary key, series text not null default '', " +
                "src text not null, ko text not null, anchor integer not null default 0, " +
                "quality text not null default 'ok', curated text, first_seen text, " +
                "last_seen text, pinned integer not null default 0)";
            create.ExecuteNonQuery();
        }

        var order = 0;
        foreach (var (series, src, ko, pinned) in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText =
                "insert into terms(series, src, ko, pinned, last_seen) values($s, $src, $ko, $p, $seen)";
            insert.Parameters.AddWithValue("$s", series);
            insert.Parameters.AddWithValue("$src", src);
            insert.Parameters.AddWithValue("$ko", ko);
            insert.Parameters.AddWithValue("$p", pinned);
            insert.Parameters.AddWithValue("$seen", $"2020-01-01 00:00:{order++:00}");
            insert.ExecuteNonQuery();
        }

        return path;
    }

    /// <summary>
    /// ★The honorific filter is a quality PROXY - measured to keep this layer free of the
    ///   machine-translation pollution the rest of the glossary carries. A row a person pinned is not a
    ///   proxy for quality, it is the thing itself, so the proxy must not exclude it.
    ///
    /// Found by pinning 由美香 -> 유미카 for real and watching the editor fail to read back what it had
    /// just written, because a given name carries no honorific.
    /// </summary>
    [Fact]
    public void AddressForms_ReadsBackAPinnedRowThatCarriesNoHonorific()
    {
        var folder = Path.Combine(Path.GetTempPath(), "terms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var saved = Se.Settings.JavData;
        try
        {
            var path = MakeGlossary(folder,
                ("NSFS", "由美香", "유미카", 1),          // pinned, no honorific
                ("NSFS", "美咲", "미사키", 0),            // same shape, not pinned
                ("NSFS", "佐々木さん", "사사키 씨", 0));    // an ordinary address form
            Se.Settings.JavData = new SeJavData { TermsDbPath = path };

            var forms = JavTerms.AddressForms("NSFS");

            Assert.Contains(forms, f => f.Source == "由美香" && f.Korean == "유미카");
            Assert.Contains(forms, f => f.Source == "佐々木さん");
            Assert.DoesNotContain(forms, f => f.Source == "美咲");
        }
        finally
        {
            Se.Settings.JavData = saved;
            Cleanup(folder);
        }
    }

    // ★And it is read back FIRST, so a person's choice cannot be crowded out of the 24-row budget by
    //   rows the machine harvested.
    [Fact]
    public void AddressForms_PutsPinnedRowsAheadOfHarvestedOnes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "terms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var saved = Se.Settings.JavData;
        try
        {
            var rows = Enumerable.Range(0, JavTerms.MaxAddressForms + 6)
                .Select(i => ("NSFS", $"田中{i}さん", $"타나카{i} 씨", 0))
                .Append(("NSFS", "ひのぼりさん", "히노보리 씨", 1))
                .ToArray();
            Se.Settings.JavData = new SeJavData { TermsDbPath = MakeGlossary(folder, rows) };

            var forms = JavTerms.AddressForms("NSFS");

            Assert.Equal(JavTerms.MaxAddressForms, forms.Count);
            Assert.Equal("ひのぼりさん", forms[0].Source);
        }
        finally
        {
            Se.Settings.JavData = saved;
            Cleanup(folder);
        }
    }

    // ★An older glossary has no pinned column. Losing the ordering is acceptable; losing the layer is
    //   not - the fallback query exists so the feature degrades instead of disappearing.
    [Fact]
    public void AddressForms_StillWorksOnAGlossaryWithoutThePinnedColumn()
    {
        var folder = Path.Combine(Path.GetTempPath(), "terms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var saved = Se.Settings.JavData;
        try
        {
            var path = Path.Combine(folder, "jav-terms.sqlite");
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "create table terms(id integer primary key, series text, src text, ko text, " +
                    "anchor integer default 0, quality text default 'ok', last_seen text);" +
                    "insert into terms(series, src, ko) values('NSFS', '佐々木さん', '사사키 씨')";
                command.ExecuteNonQuery();
            }

            Se.Settings.JavData = new SeJavData { TermsDbPath = path };

            Assert.Contains(JavTerms.AddressForms("NSFS"), f => f.Source == "佐々木さん");
        }
        finally
        {
            Se.Settings.JavData = saved;
            Cleanup(folder);
        }
    }

    // ★The glossary holds the same person as 타키모토, 타키모토 씨 and 타키모토 선생님, and all three
    //   are evidence for the same reading. 선생님 has to be stripped before 님, or the last one would
    //   look like a different name.
    [Theory]
    [InlineData("타키모토 씨", "타키모토")]
    [InlineData("타키모토", "타키모토")]
    [InlineData("타키모토 선생님", "타키모토")]
    [InlineData("칸나짱", "칸나")]
    [InlineData("이토 군", "이토")]
    [InlineData("씨", "씨")]                 // nothing but the honorific - leave it alone
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NameCore_StripsTheTrailingHonorific(string? korean, string expected)
    {
        Assert.Equal(expected, JavTerms.NameCore(korean));
    }

    /// <summary>
    /// ★The measured case this exists for: the name pass could not decide between 타키모스 씨 and
    ///   타키모토 씨, and the original-language subtitle did not settle it - it is machine-transcribed
    ///   and mis-heard the name two ways. The glossary already knew, five rows to one.
    /// </summary>
    [Fact]
    public void RankSpellings_PrefersTheReadingTheSeriesAlreadyUses()
    {
        var folder = Path.Combine(Path.GetTempPath(), "terms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var saved = Se.Settings.JavData;
        try
        {
            // The real rows from the shared glossary.
            Se.Settings.JavData = new SeJavData
            {
                TermsDbPath = MakeGlossary(folder,
                    ("APNS", "滝本", "타키모토", 0),
                    ("APNS", "滝本さん", "타키모토 씨", 0),
                    ("APNS", "滝本先生", "타키모토 씨", 0),
                    ("APNS", "Takimoto", "타키모토", 0),
                    ("APNS", "たぎもつさん", "타키모토 씨", 0),
                    ("APNS", "タキモス", "타키모스", 0)),
            };

            var ranked = JavTerms.RankSpellings("APNS", ["타키모스 씨", "타키모토 씨"]);

            Assert.Equal(2, ranked.Count);
            Assert.Equal("타키모토 씨", ranked[0].Spelling);
            Assert.Equal(5, ranked[0].Rows);
            Assert.Equal("타키모스 씨", ranked[1].Spelling);
            Assert.Equal(1, ranked[1].Rows);
        }
        finally
        {
            Se.Settings.JavData = saved;
            Cleanup(folder);
        }
    }

    // ★A pinned row wins outright rather than by count - one person's decision outranks any number of
    //   harvested rows, which is the same rule the translator's own pinned column enforces.
    [Fact]
    public void RankSpellings_PutsAPinnedSpellingFirstEvenWhenOutnumbered()
    {
        var folder = Path.Combine(Path.GetTempPath(), "terms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData
            {
                TermsDbPath = MakeGlossary(folder,
                    ("APNS", "Hinokori-san", "히노코리 씨", 0),
                    ("APNS", "Hinokori", "히노코리", 0),
                    ("APNS", "Hinokori2", "히노코리", 0),
                    ("APNS", "ひのぼりさん", "히노보리 씨", 1)),
            };

            var ranked = JavTerms.RankSpellings("APNS", ["히노코리 씨", "히노보리 씨"]);

            Assert.Equal("히노보리 씨", ranked[0].Spelling);
            Assert.True(ranked[0].Pinned);
            Assert.Equal(1, ranked[0].Rows);
            Assert.Equal(3, ranked[1].Rows);
        }
        finally
        {
            Se.Settings.JavData = saved;
            Cleanup(folder);
        }
    }

    // ★No opinion is the common case, and it must read as "no opinion" rather than "all wrong".
    [Fact]
    public void RankSpellings_IsEmptyWhenTheGlossaryKnowsNeither()
    {
        var folder = Path.Combine(Path.GetTempPath(), "terms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData
            {
                TermsDbPath = MakeGlossary(folder, ("APNS", "佐々木さん", "사사키 씨", 0)),
            };

            Assert.Empty(JavTerms.RankSpellings("APNS", ["타키모스 씨", "타키모토 씨"]));
            Assert.Empty(JavTerms.RankSpellings("", ["사사키 씨"]));
            Assert.Empty(JavTerms.RankSpellings("APNS", []));
        }
        finally
        {
            Se.Settings.JavData = saved;
            Cleanup(folder);
        }
    }

    [Fact]
    public void Pin_RefusesWhenTheGlossaryIsNotThere()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData { DataFolder = @"Z:\nothing-here", AllowWrite = true };
            Assert.False(JavTerms.Pin("HND", "佐々木さん", "사사키 씨"));
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }

    // ★Empty rather than a header with nothing under it: a prompt line that names no spellings
    //   still spends tokens and still tells the model there is a rule to follow.
    [Fact]
    public void NamesInstruction_IsEmptyWhenTheSeriesHasNoSpellings()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData { DataFolder = @"Z:\nothing-here" };
            Assert.Equal(string.Empty, JavTerms.NamesInstruction("NSFS"));
            Assert.Equal(string.Empty, JavTerms.NamesInstruction(null));
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }

    [Fact]
    public void Pin_RefusesWhenWritingIsSwitchedOff()
    {
        var saved = Se.Settings.JavData;
        try
        {
            Se.Settings.JavData = new SeJavData { AllowWrite = false };
            Assert.False(JavTerms.Pin("HND", "佐々木さん", "사사키 씨"));
        }
        finally
        {
            Se.Settings.JavData = saved;
        }
    }
}
