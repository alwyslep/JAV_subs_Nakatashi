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

    // ─── The second pass: what the original language calls this person ────────────────────────────

    private static OriginalFormQuestion Question(string korean, params (string Translated, string Original)[] lines)
        => new(korean, [], lines);

    [Fact]
    public void BuildOriginalFormRequest_PairsEachLineWithItsOriginal()
    {
        var request = NameCheckProtocol.BuildOriginalFormRequest(
            [Question("유미카", ("유미카 씨", "由美香さん"), ("유미카는", "由美香は"))]);

        Assert.Contains("NAME: 유미카", request);
        Assert.Contains("translated: 유미카 씨", request);
        Assert.Contains("original:   由美香さん", request);
        Assert.Contains("original:   由美香は", request);
    }

    [Fact]
    public void BuildOriginalFormRequest_CapsTheLinesShownPerName()
    {
        var lines = Enumerable.Range(0, 9).Select(i => ("번역" + i, "原文" + i)).ToArray();
        var request = NameCheckProtocol.BuildOriginalFormRequest([Question("이름", lines)]);

        Assert.Contains("原文" + (NameCheckProtocol.MaxLinesPerQuestion - 1), request);
        Assert.DoesNotContain("原文" + NameCheckProtocol.MaxLinesPerQuestion, request);
    }

    [Fact]
    public void ParseOriginalForms_ReadsTheSourceAndTheVerdict()
    {
        var forms = NameCheckProtocol.ParseOriginalForms(
            """{"names":[{"ko":"유미카","src":"由美香","isName":true,"fits":true},{"ko":"베핀","src":"べっぴん","isName":false,"fits":true}]}""");

        Assert.Equal("由美香", forms["유미카"].Source);
        Assert.True(forms["유미카"].IsName);
        Assert.Equal("べっぴん", forms["베핀"].Source);
        Assert.False(forms["베핀"].IsName);
    }

    // ★The measured case: the model offered 히노코리 씨 as canonical and 히노보리 씨 as the mistake,
    //   while the original said ひのぼり. Without this the "fix" writes the wrong reading over the
    //   right one - and now that the source is filled in, remembers it too.
    [Fact]
    public void ParseOriginalForms_ReadsTheSpellingVerdict()
    {
        var forms = NameCheckProtocol.ParseOriginalForms(
            """{"names":[{"ko":"히노코리 씨","src":"ひのぼりさん","isName":true,"fits":false}]}""");

        Assert.False(forms["히노코리 씨"].ChosenSpellingFits);
    }

    // ★The two flags default in opposite directions on purpose. "isName" gates a silent write, so
    //   absence must not open it. "fits" raises a visible objection, so absence must not manufacture
    //   one - a model that skips the key would otherwise unselect every good suggestion.
    [Theory]
    [InlineData("""{"names":[{"ko":"이름","src":"名前","isName":true}]}""", true)]
    [InlineData("""{"names":[{"ko":"이름","src":"名前","isName":true,"fits":"no"}]}""", true)]
    [InlineData("""{"names":[{"ko":"이름","src":"名前","isName":true,"fits":null}]}""", true)]
    [InlineData("""{"names":[{"ko":"이름","src":"名前","isName":true,"fits":false}]}""", false)]
    public void ParseOriginalForms_OnlyAnExplicitFalseDoubtsTheSpelling(string reply, bool expected)
    {
        Assert.Equal(expected, NameCheckProtocol.ParseOriginalForms(reply)["이름"].ChosenSpellingFits);
    }

    // ★This flag decides whether a spelling is written to data the translator then trusts, so a
    //   malformed or missing answer must not open the gate. The file still gets fixed either way.
    [Theory]
    [InlineData("""{"names":[{"ko":"이름","src":"名前"}]}""")]
    [InlineData("""{"names":[{"ko":"이름","src":"名前","isName":"yes"}]}""")]
    [InlineData("""{"names":[{"ko":"이름","src":"名前","isName":1}]}""")]
    [InlineData("""{"names":[{"ko":"이름","src":"名前","isName":null}]}""")]
    public void ParseOriginalForms_TreatsAnythingButTrueAsNotAName(string reply)
    {
        Assert.False(NameCheckProtocol.ParseOriginalForms(reply)["이름"].IsName);
    }

    [Fact]
    public void ParseOriginalForms_SurvivesRubbish()
    {
        Assert.Empty(NameCheckProtocol.ParseOriginalForms("no json here"));
        Assert.Empty(NameCheckProtocol.ParseOriginalForms("""{"names":[]}"""));
        Assert.Empty(NameCheckProtocol.ParseOriginalForms("""{"names":[{"src":"名前","isName":true}]}"""));
    }

    // ★The instructions say to copy the original form verbatim out of the original line. This is where
    //   that is enforced rather than trusted - a reconstructed original is worse than none, because it
    //   is what the glossary would be keyed on.
    [Fact]
    public void WithOriginalForm_RefusesASourceTheOriginalLineDoesNotContain()
    {
        var finding = Finding(string.Empty, "유미카", "유미코");

        var kept = NameCheckProtocol.WithOriginalForm(
            finding, new OriginalForm("由美香", true, true, ""), ["由美香さんですね"]);
        var refused = NameCheckProtocol.WithOriginalForm(
            finding, new OriginalForm("Yumika", true, true, ""), ["由美香さんですね"]);

        Assert.Equal("由美香", kept.Source);
        Assert.Equal(string.Empty, refused.Source);
    }

    [Fact]
    public void WithOriginalForm_LeavesTheFindingAloneWhenThereIsNoSource()
    {
        var finding = Finding("由美香", "유미카", "유미코");

        var same = NameCheckProtocol.WithOriginalForm(finding, new OriginalForm(string.Empty, true, true, ""), ["由美香"]);

        Assert.Equal("由美香", same.Source);
    }

    // ★The measured regression this rule exists for: on a film whose video tag had already given the
    //   first pass 由美香, the second pass answered 希米卡 - a Chinese transliteration read out of a
    //   subtitle named .ja.srt but written in Chinese. Filling an unknown source is the job; replacing
    //   a known one is not.
    [Fact]
    public void WithOriginalForm_NeverOverwritesASourceTheFirstPassAlreadyKnew()
    {
        var finding = Finding("由美香", "유미카", "유미코");

        var kept = NameCheckProtocol.WithOriginalForm(
            finding, new OriginalForm("希米卡", true, true, ""), ["哦希米卡"]);

        Assert.Equal("由美香", kept.Source);
    }

    // ─── Reversing a finding the original contradicts ─────────────────────────────────────────────

    // ★The measured case, twice on one film: the first pass offered 히노코리 씨 as correct with
    //   히노보리 씨 as the mistake, while the original said ひのぼり. Once the original has settled it,
    //   applying the fix the right way round beats refusing to apply it.
    [Fact]
    public void SwapDirection_TurnsTheFindingRound()
    {
        var finding = Finding("ひのぼり", "히노코리 씨", "히노보리 씨");

        var swapped = NameCheckProtocol.SwapDirection(finding, "히노보리 씨");

        Assert.NotNull(swapped);
        Assert.Equal("히노보리 씨", swapped!.Korean);
        Assert.Equal(["히노코리 씨"], swapped.Wrong);
        Assert.Equal("ひのぼり", swapped.Source);
    }

    [Fact]
    public void SwapDirection_KeepsTheOtherMistakes()
    {
        var finding = Finding("宅本", "타키모스 씨", "타키모토 씨", "타끼모토 씨");

        var swapped = NameCheckProtocol.SwapDirection(finding, "타키모토 씨");

        Assert.Equal("타키모토 씨", swapped!.Korean);
        Assert.Equal(["타끼모토 씨", "타키모스 씨"], swapped.Wrong);
    }

    // ★The second pass chooses between spellings the file actually contains. An invented one would
    //   match no line and quietly do nothing - or match the wrong one.
    [Theory]
    [InlineData("존재하지않는표기")]   // not in the reported list
    [InlineData("히노코리 씨")]        // the chosen spelling itself
    [InlineData("")]
    [InlineData(null)]
    public void SwapDirection_RefusesAnythingNotAmongTheReportedSpellings(string? better)
    {
        Assert.Null(NameCheckProtocol.SwapDirection(Finding("ひのぼり", "히노코리 씨", "히노보리 씨"), better));
    }

    // ★A reversed pair goes through exactly the same rules as an original one - that is why they share
    //   MakeFinding. Here the reversal would leave 미루짱 replacing a full name, which drops the
    //   honorific, so nothing survives and the swap is refused rather than applied badly.
    [Fact]
    public void SwapDirection_RefusesASwapThatBreaksTheFormOfAddress()
    {
        var finding = Finding("坂道みる", "미루짱", "사카미치 미루");

        Assert.Null(NameCheckProtocol.SwapDirection(finding, "사카미치 미루"));
    }

    [Fact]
    public void ParseOriginalForms_IgnoresBetterWhenTheSpellingWasAccepted()
    {
        // ★A stray "better" must not reverse a finding nobody objected to.
        var forms = NameCheckProtocol.ParseOriginalForms(
            """{"names":[{"ko":"유미카","src":"由美香","isName":true,"fits":true,"better":"유미코"}]}""");

        Assert.Equal(string.Empty, forms["유미카"].Better);
    }

    [Fact]
    public void ParseOriginalForms_KeepsBetterWhenTheSpellingWasRejected()
    {
        var forms = NameCheckProtocol.ParseOriginalForms(
            """{"names":[{"ko":"히노코리 씨","src":"ひのぼり","isName":true,"fits":false,"better":"히노보리 씨"}]}""");

        Assert.Equal("히노보리 씨", forms["히노코리 씨"].Better);
    }

    [Fact]
    public void MakeFinding_IsTheOneRuleBothDirectionsGoThrough()
    {
        // Nothing survives: 사카미치 and 미루 are contained in the chosen name.
        Assert.Null(NameCheckProtocol.MakeFinding("坂道みる", "사카미치 미루", ["사카미치", "미루"], "r"));
        // Duplicates collapse, order is kept.
        var made = NameCheckProtocol.MakeFinding("x", "사사키 씨", ["사사끼 씨", "사사끼 씨", "사사키상"], "r");
        Assert.Equal(["사사끼 씨", "사사키상"], made!.Wrong);
        Assert.Null(NameCheckProtocol.MakeFinding("x", string.Empty, ["y"], "r"));
    }

    // ★The model is told to copy the original verbatim and it does - the live run returned
    //   ひのぼりさん! with the exclamation mark, because that is how the line ended. That mark would
    //   become part of the glossary key and never match anything again.
    [Theory]
    [InlineData("ひのぼりさん!", "ひのぼりさん")]
    [InlineData("（宅本）", "宅本")]
    [InlineData("「佐々木さん」、", "佐々木さん")]
    [InlineData("  Hinokori-san.  ", "Hinokori-san")]
    [InlineData("!!!", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TrimPunctuation_KeepsTheNameAndDropsTheRest(string? value, string expected)
    {
        Assert.Equal(expected, NameCheckProtocol.TrimPunctuation(value));
    }

    [Fact]
    public void WithOriginalForm_StripsPunctuationOffACopiedSource()
    {
        var updated = NameCheckProtocol.WithOriginalForm(
            Finding(string.Empty, "히노보리 씨", "히노코리 씨"),
            new OriginalForm("ひのぼりさん!", true, true, ""), ["ひのぼりさん!"]);

        Assert.Equal("ひのぼりさん", updated.Source);
    }

    // ★"Never replace a known source" has one exception, and the live run found it: when the second
    //   pass REJECTS the chosen spelling, the first pass's source is discredited too. It answered
    //   src=Hinokori-san for a reading the original contradicts.
    [Fact]
    public void WithOriginalForm_ReplacesADiscreditedSourceWhenTheSpellingWasRejected()
    {
        var finding = Finding("Hinokori-san", "히노코리 씨", "히노보리 씨");

        var updated = NameCheckProtocol.WithOriginalForm(
            finding, new OriginalForm("ひのぼりさん", true, false, "히노보리 씨"), ["ひのぼりさん!"]);

        Assert.Equal("ひのぼりさん", updated.Source);
    }

    [Fact]
    public void BuildOriginalFormRequest_ListsTheOtherSpellingsToChooseFrom()
    {
        var request = NameCheckProtocol.BuildOriginalFormRequest(
            [new OriginalFormQuestion("히노코리 씨", ["히노보리 씨"], [("히노코리 씨!", "ひのぼりさん!")])]);

        Assert.Contains("other spellings of it in the file: 히노보리 씨", request);
    }

    // A source filled in by the second pass has to survive the pin gate too - the Hangul rule is not
    // relaxed just because a second model call vouched for it.
    [Fact]
    public void CanPin_StillRejectsAHangulSourceAfterTheSecondPass()
    {
        var finding = NameCheckProtocol.WithOriginalForm(
            Finding(string.Empty, "유미카", "유미코"), new OriginalForm("유미카씨", true, true, ""), ["유미카씨"]);

        Assert.Equal("유미카씨", finding.Source);
        Assert.False(NameCheckProtocol.CanPin(finding));
    }
}
