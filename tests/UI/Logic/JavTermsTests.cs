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
