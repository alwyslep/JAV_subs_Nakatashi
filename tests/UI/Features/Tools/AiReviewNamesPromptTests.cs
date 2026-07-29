using Nikse.SubtitleEdit.Features.Tools.AiReview;

namespace UITests.Features.Tools;

/// <summary>
/// Fork addition. The proofreading prompt now carries the series' settled name spellings.
/// ★Appended rather than substituted, so a user who customised their prompt - which the tool
///   invites them to do - still gets the constraint.
/// </summary>
public class AiReviewNamesPromptTests
{
    private const string Instructions = "Fix typos in {language}.";

    [Fact]
    public void BuildSystemPrompt_AppendsTheNamesInstruction()
    {
        var prompt = AiReviewProtocol.BuildSystemPrompt(
            Instructions, "Korean", "how this series writes names - keep these spellings exactly: 佐々木さん = 사사키 씨");

        Assert.Contains("Fix typos in Korean.", prompt);
        Assert.Contains("佐々木さん = 사사키 씨", prompt);
        // The wire protocol has to stay last - it is what tells the model the answer shape.
        Assert.EndsWith(AiReviewProtocol.ProtocolText, prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithNoNamesIsUnchanged()
    {
        var withoutArgument = AiReviewProtocol.BuildSystemPrompt(Instructions, "Korean");

        Assert.Equal(withoutArgument, AiReviewProtocol.BuildSystemPrompt(Instructions, "Korean", null));
        Assert.Equal(withoutArgument, AiReviewProtocol.BuildSystemPrompt(Instructions, "Korean", "   "));
    }

    // A customised prompt has no placeholder to substitute into, so appending is the only way it
    // can receive this at all.
    [Fact]
    public void BuildSystemPrompt_ReachesACustomisedPromptToo()
    {
        var prompt = AiReviewProtocol.BuildSystemPrompt(
            "내 마음대로 고친 지시문", "Korean", "names: A = B");

        Assert.Contains("내 마음대로 고친 지시문", prompt);
        Assert.Contains("names: A = B", prompt);
    }
}
