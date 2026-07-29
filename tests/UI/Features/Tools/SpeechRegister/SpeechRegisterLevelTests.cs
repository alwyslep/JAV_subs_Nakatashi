using Nikse.SubtitleEdit.Features.Tools.SpeechRegister;
using Xunit;

namespace Tests.UI.Features.Tools.SpeechRegister;

public class SpeechRegisterLevelTests
{
    [Theory]
    [InlineData("deferential", SpeechLevel.Deferential)]
    [InlineData("polite", SpeechLevel.Polite)]
    [InlineData("casual", SpeechLevel.Casual)]
    [InlineData("plain", SpeechLevel.Plain)]
    public void Token_round_trips(string token, SpeechLevel level)
    {
        Assert.Equal(level, SpeechLevels.Parse(token));
        Assert.Equal(token, SpeechLevels.Token(level));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("해요체")]      // a localized name must never reach Parse
    [InlineData("garbage")]
    public void Unknown_tokens_fall_back_to_polite(string? token)
    {
        // ★A settings file from another version must not crash or silently pick 반말.
        Assert.Equal(SpeechLevel.Polite, SpeechLevels.Parse(token));
    }

    [Theory]
    [InlineData("갈게", "갑니다")]
    [InlineData("가", "가십시오")]
    [InlineData("알겠어", "알겠습니다")]
    [InlineData("고마워", "고맙습니다")]
    [InlineData("맞아", "맞아요")]
    [InlineData("알았어", "알았습니다")]
    [InlineData("여기 있어", "여기 있습니다")]
    public void Only_the_ending_changed_is_not_flagged(string before, string after)
    {
        // ★These are exactly what the tool is for. AI review's length-ratio rule
        //   (>1.4 or <0.6) would flag "가" -> "가십시오" at ratio 4.0, which is why it
        //   cannot be reused here.
        Assert.False(SpeechLevels.StemChanged(before, after));
    }

    [Theory]
    [InlineData("어제 봤어", "오늘 봤어요")]                 // 어제 -> 오늘
    [InlineData("고등학생이야", "대학생이에요")]              // 고등 -> 대학
    [InlineData("혀 색 좀 봐 줘", "손 좀 봐 주세요")]         // whole phrase rewritten
    public void A_rewritten_stem_is_flagged(string before, string after)
    {
        Assert.True(SpeechLevels.StemChanged(before, after));
    }

    [Fact]
    public void Punctuation_and_spacing_do_not_count_as_a_stem_change()
    {
        Assert.False(SpeechLevels.StemChanged("정말 괜찮아?", "정말 괜찮아요?"));
        Assert.False(SpeechLevels.StemChanged("네, 알겠어", "네 알겠습니다."));
    }

    [Fact]
    public void An_empty_side_is_flagged_rather_than_trusted()
    {
        Assert.True(SpeechLevels.StemChanged("알겠어", ""));
        Assert.True(SpeechLevels.StemChanged("", "알겠습니다"));
    }
}
