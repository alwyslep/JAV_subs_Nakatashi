using Nikse.SubtitleEdit.Logic.JavData;

namespace UITests.Logic;

/// <summary>
/// Fork addition. Covers the assembly rules of the per-film STT prompt. The measured behaviour it
/// exists to protect - that a prompt suppresses Whisper's non-speech hallucination - cannot be
/// asserted here; what can be, and is, are the rules that decide what goes into it and in what
/// order.
/// </summary>
public class SttPromptTests
{
    private static readonly string[] None = [];

    private const string Seed = "うん、気持ちいい。";

    [Fact]
    public void Assemble_KeepsTheSeedInFrontOfTheNames()
    {
        var prompt = SttPrompt.Assemble(Seed, ["大島優香"], None);

        Assert.StartsWith(Seed, prompt);
        Assert.Contains("大島優香", prompt);
    }

    // ★The decoder weights the tail of the prompt more heavily, and dialogue uses what a character
    //   is CALLED far more often than a performer's legal name - so address forms must end up
    //   nearest the end, after the cast.
    [Fact]
    public void Assemble_PutsAddressFormsAfterTheCast()
    {
        var prompt = SttPrompt.Assemble(Seed, ["大島優香"], ["石上さとみ"]);

        Assert.True(prompt.IndexOf("大島優香", System.StringComparison.Ordinal)
                    < prompt.IndexOf("石上さとみ", System.StringComparison.Ordinal));
    }

    // Returning empty means "keep what is configured". Overwriting the setting with a blank would
    // strip the seed, which is the thing actually holding the hallucination down.
    [Fact]
    public void Assemble_IsEmptyWhenThereAreNoNamesToAdd()
    {
        Assert.Equal(string.Empty, SttPrompt.Assemble(Seed, None, None));
        Assert.Equal(string.Empty, SttPrompt.Assemble(string.Empty, None, None));
    }

    // The fork's hard rule about names, applied a second time: these came from a different table
    // than the cast list, with its own history.
    [Fact]
    public void Assemble_DropsHangulNames()
    {
        var prompt = SttPrompt.Assemble(Seed, ["린의 집 인"], ["새우하라"]);

        Assert.Equal(string.Empty, prompt);
    }

    // ★Narrower than the glossary's own pin gate on purpose. 11.9% of usable glossary rows are
    //   Latin-only, and they are legitimate there - but "Takimoto" in a Japanese prompt biases the
    //   decoder toward emitting Latin script into a Japanese transcript.
    [Fact]
    public void Assemble_DropsRomanisedNamesEvenThoughTheGlossaryAcceptsThem()
    {
        Assert.Equal(string.Empty, SttPrompt.Assemble(Seed, None, ["Takimoto"]));

        // The same spelling is a perfectly good glossary key - the gates differ because the uses do.
        Assert.True(JavTerms.IsAddressForm("滝本さん", "타키모토 씨"));
    }

    [Fact]
    public void Assemble_AcceptsKanjiKanaAndKatakana()
    {
        Assert.True(SttPrompt.IsUsableJapanese("滝本"));
        Assert.True(SttPrompt.IsUsableJapanese("さとみ"));
        Assert.True(SttPrompt.IsUsableJapanese("ミツキ"));
        Assert.True(SttPrompt.IsUsableJapanese("鈴の家りん"));
        Assert.False(SttPrompt.IsUsableJapanese("TECH"));
        Assert.False(SttPrompt.IsUsableJapanese("모모타 미츠키"));
        Assert.False(SttPrompt.IsUsableJapanese("123"));

        // Real harvest debris from the ABF series' glossary - it reached the prompt before the
        // digit rule existed, spending one of the twelve slots.
        Assert.False(SttPrompt.IsUsableJapanese("6ファイルさん"));
        Assert.False(SttPrompt.IsUsableJapanese("６号さん"));
    }

    [Fact]
    public void Assemble_DeduplicatesAcrossBothLists()
    {
        var prompt = SttPrompt.Assemble(Seed, ["滝本", "滝本"], ["滝本"]);

        var first = prompt.IndexOf("滝本", System.StringComparison.Ordinal);
        var last = prompt.LastIndexOf("滝本", System.StringComparison.Ordinal);
        Assert.Equal(first, last);
    }

    // ★Overrunning 224 tokens is not a loud failure - Whisper keeps the tail, which would silently
    //   drop the seed. So the cap has to hold with the seed already counted against it.
    [Fact]
    public void Assemble_StaysInsideTheCharacterBudget()
    {
        var many = new string[40];
        for (var i = 0; i < many.Length; i++)
        {
            many[i] = "長谷川あかり" + (char)('あ' + i);
        }

        var prompt = SttPrompt.Assemble(new string('あ', 120), many, None);

        Assert.True(prompt.Length <= SttPrompt.MaxCharacters,
            $"prompt was {prompt.Length} characters, cap is {SttPrompt.MaxCharacters}");
    }

    [Fact]
    public void Assemble_CarriesAtMostMaxNames()
    {
        var many = new string[SttPrompt.MaxNames + 8];
        for (var i = 0; i < many.Length; i++)
        {
            many[i] = "あ" + (char)('ア' + i);
        }

        var prompt = SttPrompt.Assemble(string.Empty, many, None);

        Assert.Equal(SttPrompt.MaxNames - 1, prompt.Split('、').Length - 1);
    }

    [Fact]
    public void Assemble_ClosesTheRunSoItReadsAsJapaneseText()
    {
        Assert.EndsWith("。", SttPrompt.Assemble(Seed, ["滝本"], None));
    }

    // A file that is not in the library at all must not throw, and must not invent a prompt.
    [Fact]
    public void Build_ReturnsEmptyForNoVideo()
    {
        Assert.Equal(string.Empty, SttPrompt.Build(null, Seed));
        Assert.Equal(string.Empty, SttPrompt.Build(string.Empty, Seed));
    }
}
