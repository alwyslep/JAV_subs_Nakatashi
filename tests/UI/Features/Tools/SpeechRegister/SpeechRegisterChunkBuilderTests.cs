using Nikse.SubtitleEdit.Features.Tools.AiReview;
using Nikse.SubtitleEdit.Features.Tools.SpeechRegister;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Tests.UI.Features.Tools.SpeechRegister;

public class SpeechRegisterChunkBuilderTests
{
    private static List<ReviewLine> Lines(int count) =>
        Enumerable.Range(1, count).Select(i => new ReviewLine(i, "줄 " + i + "이야")).ToList();

    [Fact]
    public void Context_comes_from_the_whole_subtitle_not_just_the_selection()
    {
        // ★AiReviewChunker takes its context from the list it is handed. Feed it only the
        //   selection and the "surrounding dialogue" is more selected lines - useless for
        //   deciding who is speaking to whom.
        var all = Lines(20);
        var chunks = SpeechRegisterChunkBuilder.Build(all, new HashSet<int> { 10, 11 }, 10);

        var chunk = Assert.Single(chunks);
        Assert.Equal(new[] { 10, 11 }, chunk.Lines.Select(l => l.Number));
        Assert.Equal(new[] { 8, 9 }, chunk.ContextBefore.Select(l => l.Number));
        Assert.Equal(new[] { 12, 13 }, chunk.ContextAfter.Select(l => l.Number));
    }

    [Fact]
    public void A_gap_in_the_selection_never_puts_unrelated_lines_in_one_batch()
    {
        // ★BuildUnitIds joins a line to the next one whenever it does not end a sentence, and it
        //   walks the list it is given. Without splitting on the gap first, line 11 and line 400
        //   would become one "sentence unit".
        var all = Lines(500);
        var chunks = SpeechRegisterChunkBuilder.Build(all, new HashSet<int> { 10, 11, 400, 401 }, 10);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(new[] { 10, 11 }, chunks[0].Lines.Select(l => l.Number));
        Assert.Equal(new[] { 400, 401 }, chunks[1].Lines.Select(l => l.Number));
    }

    [Fact]
    public void Context_clamps_at_the_edges_of_the_file()
    {
        var all = Lines(3);
        var chunks = SpeechRegisterChunkBuilder.Build(all, new HashSet<int> { 1 }, 10);

        var chunk = Assert.Single(chunks);
        Assert.Empty(chunk.ContextBefore);
        Assert.Equal(new[] { 2, 3 }, chunk.ContextAfter.Select(l => l.Number));
    }

    [Fact]
    public void Every_selected_line_survives_batching()
    {
        var all = Lines(200);
        var selected = new HashSet<int>(Enumerable.Range(20, 90));

        var chunks = SpeechRegisterChunkBuilder.Build(all, selected, 7);
        var batched = chunks.SelectMany(c => c.Lines).Select(l => l.Number).ToList();

        // No line lost, none duplicated, order preserved - a dropped line is a silently
        // unprocessed line, which the user has no way to notice.
        Assert.Equal(selected.OrderBy(n => n), batched);
    }

    [Fact]
    public void Empty_selection_produces_nothing()
    {
        Assert.Empty(SpeechRegisterChunkBuilder.Build(Lines(10), new HashSet<int>(), 10));
    }

    [Fact]
    public void Selection_numbers_that_do_not_exist_are_ignored()
    {
        var chunks = SpeechRegisterChunkBuilder.Build(Lines(5), new HashSet<int> { 3, 99 }, 10);
        var chunk = Assert.Single(chunks);
        Assert.Equal(new[] { 3 }, chunk.Lines.Select(l => l.Number));
    }
}
