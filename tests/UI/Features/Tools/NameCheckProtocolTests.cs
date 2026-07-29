using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Tools.NameCheck;

namespace UITests.Features.Tools;

/// <summary>
/// Fork addition. The model here only says which spelling replaces which; the editor does the
/// substitution. These cover the half the editor owns - which is the half that must not go wrong.
/// </summary>
public class NameCheckProtocolTests
{
    private static Subtitle Make(params string[] texts)
    {
        var subtitle = new Subtitle();
        foreach (var text in texts)
        {
            subtitle.Paragraphs.Add(new Paragraph(text, 0, 1000));
        }

        return subtitle;
    }

    private static NameFinding Finding(string src, string ko, params string[] wrong)
        => new(src, ko, wrong, "reason");

    [Fact]
    public void ParseNames_ReadsTheTable()
    {
        var found = NameCheckProtocol.ParseNames(
            """{"names":[{"src":"佐々木さん","ko":"사사키 씨","wrong":["사사끼 씨","사사키상"],"reason":"둘로 갈림"}]}""");

        Assert.Single(found);
        Assert.Equal("佐々木さん", found[0].Source);
        Assert.Equal("사사키 씨", found[0].Korean);
        Assert.Equal(["사사끼 씨", "사사키상"], found[0].Wrong);
    }

    // ★Left in, the replacement is a no-op that still shows as a suggestion, and the user is asked
    //   to approve a change that changes nothing.
    [Fact]
    public void ParseNames_DropsTheChosenSpellingFromItsOwnWrongList()
    {
        var found = NameCheckProtocol.ParseNames(
            """{"names":[{"ko":"사사키 씨","wrong":["사사키 씨","사사끼 씨","사사끼 씨"]}]}""");

        Assert.Equal(["사사끼 씨"], found[0].Wrong);
    }

    // ★The measured failure this guard exists for: the model offered 사카미치 미루 as canonical with
    //   사카미치 and 미루 among the wrong spellings. Replacing a string with something that contains
    //   it feeds itself - one line came back "사카미치 사카미치 미루 사카미치 미루".
    [Fact]
    public void ParseNames_DropsSpellingsContainedInTheChosenOne()
    {
        // Every entry is rejected here: 사카미치 and 미루 are contained in the chosen name, and
        // 미르짱 would lose its honorific to it - so the finding disappears rather than corrupting.
        Assert.Empty(NameCheckProtocol.ParseNames(
            """{"names":[{"src":"坂道みる","ko":"사카미치 미루","wrong":["사카미치","미루","미르짱"]}]}"""));

        // With a like-for-like replacement the substring guard still does its job.
        var found = NameCheckProtocol.ParseNames(
            """{"names":[{"src":"坂道みる","ko":"미루짱","wrong":["미루","미르짱"]}]}""");
        Assert.Equal(["미르짱"], found[0].Wrong);
    }

    [Fact]
    public void ParseNames_DropsAFindingLeftWithNothingToReplace()
    {
        // Every "wrong" was an abbreviation of the chosen name - there is no misspelling here.
        Assert.Empty(NameCheckProtocol.ParseNames(
            """{"names":[{"ko":"사카미치 미루","wrong":["사카미치","미루"]}]}"""));
    }

    // ★The substitution is word for word, so the form of address has to survive it. Measured: the
    //   model answered 미르짱 -> 사카미치 미루 and the lines came back "사카미치 미루이 그런 일은",
    //   "사카미치 미루은, 반드시" - a full name in a slot a nickname was holding.
    [Theory]
    [InlineData("미르짱", "미루짱", true)]
    [InlineData("미르짱", "사카미치 미루", false)]   // honorific dropped
    [InlineData("사사키상", "사사키 씨", true)]       // 상 is さん half-transliterated - fixing it is the point
    [InlineData("사미지마 군", "사미지마 씨", false)] // which honorific is the relationship, not a typo
    [InlineData("사미지마 군", "사시지마 군", true)]
    [InlineData("히라타", "히라따", true)]
    [InlineData("히라타", "히라타 씨", false)]
    public void KeepsFormOfAddress_RequiresALikeForLikeFix(string wrong, string korean, bool expected)
    {
        Assert.Equal(expected, NameCheckProtocol.KeepsFormOfAddress(wrong, korean));
    }

    [Fact]
    public void ParseNames_DropsAReplacementThatChangesTheFormOfAddress()
    {
        Assert.Empty(NameCheckProtocol.ParseNames(
            """{"names":[{"src":"坂道みる","ko":"사카미치 미루","wrong":["미르짱"]}]}"""));

        var kept = NameCheckProtocol.ParseNames(
            """{"names":[{"src":"坂道みる","ko":"미루짱","wrong":["미르짱"]}]}""");
        Assert.Equal(["미르짱"], kept[0].Wrong);
    }

    [Fact]
    public void ParseNames_SkipsEntriesThatCannotChangeAnything()
    {
        Assert.Empty(NameCheckProtocol.ParseNames("""{"names":[{"ko":"사사키 씨","wrong":[]}]}"""));
        Assert.Empty(NameCheckProtocol.ParseNames("""{"names":[{"src":"佐々木さん","wrong":["x"]}]}"""));
        Assert.Empty(NameCheckProtocol.ParseNames("""{"names":[]}"""));
        Assert.Empty(NameCheckProtocol.ParseNames("모델이 그냥 말로 답했다"));
        Assert.Empty(NameCheckProtocol.ParseNames(""));
    }

    [Fact]
    public void BuildReplacements_ChangesOnlyTheLinesThatContainTheSpelling()
    {
        var subtitle = Make("사사끼 씨, 안녕하세요", "오늘은 날씨가 좋네요", "사사끼 씨는요?");
        var result = NameCheckProtocol.BuildReplacements(subtitle, [Finding("佐々木さん", "사사키 씨", "사사끼 씨")]);

        Assert.Equal(2, result.Count);
        Assert.Equal("사사키 씨, 안녕하세요", result[0].After);
        Assert.Equal(1, result[0].Number);
        Assert.Equal("사사키 씨는요?", result[1].After);
    }

    // ★A spelling the model imagined simply matches nothing and disappears - it never becomes a
    //   suggestion, because the substitution runs against the real text.
    [Fact]
    public void BuildReplacements_IgnoresASpellingThatIsNotInTheText()
    {
        var result = NameCheckProtocol.BuildReplacements(
            Make("사사키 씨, 안녕하세요"), [Finding("佐々木さん", "사사키 씨", "존재하지않는표기")]);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildReplacements_AppliesSeveralNamesToOneLine()
    {
        var result = NameCheckProtocol.BuildReplacements(
            Make("사사끼 씨와 아즈 쨩"),
            [Finding("佐々木さん", "사사키 씨", "사사끼 씨"), Finding("あずちゃん", "아즈 짱", "아즈 쨩")]);

        Assert.Single(result);
        Assert.Equal("사사키 씨와 아즈 짱", result[0].After);
    }

    // ★A name fix can never need to move a formatting tag, and a replacement that swallowed an
    //   <i> would corrupt the line with nothing on screen to show it.
    [Fact]
    public void BuildReplacements_RefusesToDisturbFormattingTags()
    {
        // The replacement swallows the closing tag: 사사끼 씨</i> -> 사사키 씨.
        var result = NameCheckProtocol.BuildReplacements(
            Make("<i>사사끼 씨</i>"), [Finding("佐々木さん", "사사키 씨", "사사끼 씨</i>")]);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildReplacements_KeepsTagsThatAreNotTouched()
    {
        var result = NameCheckProtocol.BuildReplacements(
            Make("<i>사사끼 씨</i>"), [Finding("佐々木さん", "사사키 씨", "사사끼 씨")]);

        Assert.Single(result);
        Assert.Equal("<i>사사키 씨</i>", result[0].After);
    }

    // ★The glossary gate: a source already in Hangul went through a machine translation, so it is
    //   not the original spelling of anything. Such a finding still fixes the file.
    [Theory]
    [InlineData("佐々木さん", "사사키 씨", true)]
    [InlineData("Sushi-san", "스시 씨", true)]
    [InlineData("린의 집 인", "스즈노야 린", false)]
    [InlineData("", "사사키 씨", false)]
    [InlineData("佐々木さん", "", false)]
    public void CanPin_GuardsWhatMayEnterTheGlossary(string source, string korean, bool expected)
    {
        Assert.Equal(expected, NameCheckProtocol.CanPin(Finding(source, korean, "x")));
    }

    [Fact]
    public void BuildUserContent_IsOneLinePerSubtitleAndIsCapped()
    {
        var content = NameCheckProtocol.BuildUserContent(Make("첫 줄", "   ", "둘째 줄"));

        Assert.Equal("첫 줄\n둘째 줄\n", content);

        var huge = Make(Enumerable.Range(0, 400).Select(i => i + new string('가', 500)).ToArray());
        Assert.True(NameCheckProtocol.BuildUserContent(huge).Length <= NameCheckProtocol.MaxDialogueChars);
    }

    // ★A repeated line answers "which spellings are in this file" exactly as well the first time.
    //   Measured on a 6,582-line subtitle the raw text hit the cap around line 3,000, so a name
    //   introduced after that could not be found at all.
    [Fact]
    public void BuildUserContent_SendsEachDistinctLineOnce()
    {
        var content = NameCheckProtocol.BuildUserContent(
            Make("미루짱", "응", "미루짱", "응", "미루짱", "다른 줄"));

        Assert.Equal("미루짱\n응\n다른 줄\n", content);
    }

    [Fact]
    public void BuildUserContent_KeepsFirstOccurrenceOrder()
    {
        // The model still has to see who is talking to whom; shuffling would take that away.
        var content = NameCheckProtocol.BuildUserContent(Make("가", "나", "가", "다"));

        Assert.Equal("가\n나\n다\n", content);
    }

    // ★The transliteration rule is the one rule of this pass; a prompt edit that dropped it would
    //   turn the tool into the thing it exists to undo, so it lives outside the editable part.
    [Fact]
    public void BuildSystemPrompt_AlwaysCarriesTheTransliterationRuleAndTheFormat()
    {
        var prompt = NameCheckProtocol.BuildSystemPrompt("내가 통째로 갈아치운 지시문", "Korean", null);

        Assert.Contains("내가 통째로 갈아치운 지시문", prompt);
        Assert.Contains("TRANSLITERATED, never translated", prompt);
        Assert.EndsWith(NameCheckProtocol.ProtocolText, prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesTheFilmContextWhenThereIsOne()
    {
        var prompt = NameCheckProtocol.BuildSystemPrompt("x {language}", "Korean", "credited: 大島優香");

        Assert.Contains("x Korean", prompt);
        Assert.Contains("credited: 大島優香", prompt);
    }
}
