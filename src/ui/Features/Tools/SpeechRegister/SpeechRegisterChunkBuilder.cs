using Nikse.SubtitleEdit.Features.Tools.AiReview;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Tools.SpeechRegister;

/// <summary>
/// Batches the <b>selected</b> lines for the speech-level pass.
///
/// This wraps <see cref="AiReviewChunker"/> instead of calling it directly, because two of its
/// assumptions break once the input is a selection rather than the whole file:
///
/// ①<b>Context is taken from the input list only</b> (AiReviewChunker.BuildChunks builds its
///   index from <c>lines</c> and reads <c>lines[k]</c> for the read-only context). Hand it the
///   selection and the "surrounding dialogue" is just more selected lines. Speech level is
///   decided by who is talking to whom, and that evidence sits in the lines <i>around</i> the
///   selection - so the context is refilled here from the full subtitle.
///
/// ②<b>Sentence units run straight through a gap.</b> BuildUnitIds walks the list in order and
///   joins a line to the next whenever it does not end a sentence. For a selection of
///   {10, 11, 400, 401} that means line 11 and line 400 can land in the same "sentence", which
///   puts unrelated dialogue in one batch. So the selection is split into contiguous runs first
///   and each run is batched on its own.
/// </summary>
public static class SpeechRegisterChunkBuilder
{
    /// <summary>Read-only lines added on each side of a batch, taken from the full subtitle.</summary>
    public const int ContextLines = 2;

    /// <param name="allLines">Every line of the subtitle, in order, numbered 1-based.</param>
    /// <param name="selectedNumbers">Line numbers the user selected.</param>
    public static List<ReviewChunk> Build(IReadOnlyList<ReviewLine> allLines, ISet<int> selectedNumbers, int maxLinesPerChunk)
    {
        var chunks = new List<ReviewChunk>();
        if (allLines.Count == 0 || selectedNumbers.Count == 0)
        {
            return chunks;
        }

        var indexByNumber = new Dictionary<int, int>(allLines.Count);
        for (var i = 0; i < allLines.Count; i++)
        {
            indexByNumber[allLines[i].Number] = i;
        }

        foreach (var run in ContiguousRuns(allLines, selectedNumbers, indexByNumber))
        {
            foreach (var chunk in AiReviewChunker.BuildChunks(run, maxLinesPerChunk))
            {
                RefillContext(chunk, allLines, indexByNumber);
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    /// <summary>Selected lines split wherever the selection skips a line in the real subtitle.</summary>
    private static IEnumerable<List<ReviewLine>> ContiguousRuns(
        IReadOnlyList<ReviewLine> allLines, ISet<int> selectedNumbers, Dictionary<int, int> indexByNumber)
    {
        var ordered = selectedNumbers
            .Where(indexByNumber.ContainsKey)
            .OrderBy(n => indexByNumber[n])
            .ToList();

        var run = new List<ReviewLine>();
        var previousIndex = int.MinValue;
        foreach (var number in ordered)
        {
            var index = indexByNumber[number];
            if (run.Count > 0 && index != previousIndex + 1)
            {
                yield return run;
                run = new List<ReviewLine>();
            }

            run.Add(allLines[index]);
            previousIndex = index;
        }

        if (run.Count > 0)
        {
            yield return run;
        }
    }

    /// <summary>Replaces the run-local context with the real neighbours from the whole subtitle.</summary>
    private static void RefillContext(ReviewChunk chunk, IReadOnlyList<ReviewLine> allLines, Dictionary<int, int> indexByNumber)
    {
        chunk.ContextBefore.Clear();
        chunk.ContextAfter.Clear();
        if (chunk.Lines.Count == 0)
        {
            return;
        }

        var first = indexByNumber[chunk.Lines[0].Number];
        var last = indexByNumber[chunk.Lines[^1].Number];

        for (var i = System.Math.Max(0, first - ContextLines); i < first; i++)
        {
            chunk.ContextBefore.Add(allLines[i]);
        }

        for (var i = last + 1; i <= System.Math.Min(allLines.Count - 1, last + ContextLines); i++)
        {
            chunk.ContextAfter.Add(allLines[i]);
        }
    }
}
