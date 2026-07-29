using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Nikse.SubtitleEdit;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Tools.SpeechRegister;
using Nikse.SubtitleEdit.Logic.Config;
using System.Linq;
using Xunit;

namespace Tests.UI.Features.Tools.SpeechRegister;

/// <summary>
/// The feature is wired through DI, settings and language resources. Each of those can be
/// forgotten independently and none of them fails at compile time - the first sign would be the
/// window throwing when the user opens it.
/// </summary>
public class SpeechRegisterWiringTests
{
    private static SpeechRegisterViewModel Resolve()
    {
        var services = new ServiceCollection();
        services.AddSubtitleEditServices();
        return services.BuildServiceProvider().GetRequiredService<SpeechRegisterViewModel>();
    }

    [AvaloniaFact]
    public void View_model_resolves_from_the_container()
    {
        // Catches a missing AddTransient and a constructor that trips over uninitialized
        // Se.Language / Se.Settings.
        var vm = Resolve();
        Assert.NotNull(vm);
        Assert.Equal(4, vm.Levels.Count);
        Assert.Contains(vm.SelectedLevel, vm.Levels);
    }

    [AvaloniaFact]
    public void Settings_and_language_sections_exist()
    {
        // A new section added to the C# class but not to SeTools/LanguageTools compiles fine and
        // is null at runtime.
        Assert.NotNull(Se.Settings.Tools.SpeechRegister);
        Assert.NotNull(Se.Language.Tools.SpeechRegister);
        Assert.False(string.IsNullOrWhiteSpace(Se.Language.Tools.SpeechRegister.Title));
        Assert.False(string.IsNullOrWhiteSpace(Se.Language.Tools.SpeechRegister.MenuItem));
    }

    [AvaloniaFact]
    public void Only_selected_lines_are_offered_for_change()
    {
        // ★A batch also carries read-only context lines. ParseChanges keeps changes inside the
        //   batch, which includes nothing outside it - but the context lines belong to the batch's
        //   neighbourhood, and a model that "helpfully" fixes one of those must not reach the user.
        var subtitle = new Subtitle();
        for (var i = 0; i < 10; i++)
        {
            subtitle.Paragraphs.Add(new Paragraph("줄 " + i + "이야", i * 1000, i * 1000 + 900));
        }

        var vm = Resolve();
        vm.Initialize(subtitle, new[] { 2, 3 });

        Assert.Contains("2", vm.SelectionText);
    }

    [Fact]
    public void The_prompt_carries_the_level_and_the_notes()
    {
        var prompt = SpeechRegisterPrompt.BuildSystemPrompt(
            SeSpeechRegister.DefaultPrompt, SpeechLevel.Deferential, "아내→상층부 해요체", "Korean");

        Assert.Contains("하십시오체", prompt);          // {level} was substituted, not left as a token
        Assert.Contains("아내→상층부 해요체", prompt);   // {notes} likewise
        Assert.Contains("Korean", prompt);
        Assert.DoesNotContain("{level}", prompt);
        Assert.DoesNotContain("{notes}", prompt);
        Assert.DoesNotContain("{language}", prompt);
        Assert.Contains("\"changes\"", prompt);         // the wire contract ParseChanges expects
    }

    [Fact]
    public void An_empty_note_leaves_no_dangling_heading()
    {
        var prompt = SpeechRegisterPrompt.BuildSystemPrompt(
            SeSpeechRegister.DefaultPrompt, SpeechLevel.Polite, "   ", "Korean");

        Assert.DoesNotContain("Who speaks how", prompt);
        Assert.DoesNotContain("{notes}", prompt);
    }

    [Theory]
    [InlineData("알겠어", "")]                                  // empty
    [InlineData("알겠어", "알겠어")]                            // unchanged
    [InlineData("<i>알겠어</i>", "알겠습니다")]                  // tag dropped
    [InlineData("알겠어", "<i>알겠습니다</i>")]                  // tag invented
    public void Broken_suggestions_are_dropped_before_the_user_sees_them(string before, string after)
    {
        Assert.True(SpeechRegisterPrompt.ShouldDrop(before, after));
    }

    [Fact]
    public void A_good_suggestion_survives_with_its_tags()
    {
        Assert.False(SpeechRegisterPrompt.ShouldDrop("<i>알겠어</i>", "<i>알겠습니다</i>"));
        Assert.False(SpeechRegisterPrompt.ShouldDrop("알겠어", "알겠습니다"));
    }

    [Fact]
    public void Default_prompt_is_used_when_the_stored_one_was_emptied()
    {
        var saved = Se.Settings.Tools.SpeechRegister.Prompt;
        try
        {
            Se.Settings.Tools.SpeechRegister.Prompt = "   ";
            Assert.Equal(SeSpeechRegister.DefaultPrompt, SpeechRegisterPrompt.EffectivePrompt());
        }
        finally
        {
            Se.Settings.Tools.SpeechRegister.Prompt = saved;
        }
    }
}
