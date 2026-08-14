using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Core.Publishing;

/// <summary>
/// Word-level difference between two pieces of text (spec section 11.4).
/// </summary>
/// <remarks>
/// Words rather than characters, because the reader is a person checking what an editor changed in a
/// paragraph. A character diff of a rewritten sentence is a cloud of single letters; a word diff
/// shows the sentence.
/// </remarks>
public static class WordDiff
{
    /// <summary>
    /// Longest input either side may have before the diff degrades to a wholesale replacement.
    /// </summary>
    /// <remarks>
    /// The comparison is quadratic in the number of words, which is the right algorithm for a
    /// paragraph and the wrong one for a pasted book. Beyond this the result is reported as one
    /// removal and one addition — still correct, just less useful — rather than as a request that
    /// ties up a thread. Ten thousand words is several times longer than any zone anybody edits by
    /// hand.
    /// </remarks>
    public const int MaxWords = 10_000;

    /// <summary>
    /// Compares two pieces of text.
    /// </summary>
    /// <param name="before">The earlier text, or null.</param>
    /// <param name="after">The later text, or null.</param>
    /// <returns>
    /// Consecutive runs of unchanged, removed, and added text, in reading order: everything removed
    /// at a position comes before everything added there, so a side-by-side view can lay the two
    /// columns out from one list.
    /// </returns>
    public static IReadOnlyList<TextSegment> Compute(string? before, string? after)
    {
        var left = Split(before);
        var right = Split(after);

        if (left.Length == 0 && right.Length == 0) return [];

        if (left.Length > MaxWords || right.Length > MaxWords)
        {
            return Wholesale(before, after);
        }

        // Common prefix and suffix are stripped first. Most edits touch a few words in the middle of
        // an unchanged paragraph, and trimming makes the quadratic step run over what actually
        // differs rather than over the whole zone.
        var start = 0;
        while (start < left.Length && start < right.Length &&
            string.Equals(left[start], right[start], StringComparison.Ordinal))
        {
            start++;
        }

        var endLeft = left.Length - 1;
        var endRight = right.Length - 1;
        while (endLeft >= start && endRight >= start &&
            string.Equals(left[endLeft], right[endRight], StringComparison.Ordinal))
        {
            endLeft--;
            endRight--;
        }

        var segments = new List<TextSegment>();

        Append(segments, ContentChangeKind.Unchanged, left, 0, start);
        AppendMiddle(segments, left, right, start, endLeft, endRight);
        Append(segments, ContentChangeKind.Unchanged, left, endLeft + 1, left.Length);

        return Merge(segments);
    }

    /// <summary>Runs the longest-common-subsequence walk over the part that actually differs.</summary>
    private static void AppendMiddle(
        List<TextSegment> segments,
        string[] left,
        string[] right,
        int start,
        int endLeft,
        int endRight)
    {
        var leftCount = endLeft - start + 1;
        var rightCount = endRight - start + 1;

        if (leftCount <= 0 && rightCount <= 0) return;

        if (leftCount <= 0)
        {
            Append(segments, ContentChangeKind.Added, right, start, endRight + 1);

            return;
        }

        if (rightCount <= 0)
        {
            Append(segments, ContentChangeKind.Removed, left, start, endLeft + 1);

            return;
        }

        var lengths = new int[leftCount + 1, rightCount + 1];

        for (var i = leftCount - 1; i >= 0; i--)
        {
            for (var j = rightCount - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(left[start + i], right[start + j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var x = 0;
        var y = 0;

        while (x < leftCount && y < rightCount)
        {
            if (string.Equals(left[start + x], right[start + y], StringComparison.Ordinal))
            {
                segments.Add(new TextSegment(left[start + x], ContentChangeKind.Unchanged));
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                // Removals are emitted before additions at the same position, so a replaced phrase
                // reads as the old text followed by the new rather than interleaved.
                segments.Add(new TextSegment(left[start + x], ContentChangeKind.Removed));
                x++;
            }
            else
            {
                segments.Add(new TextSegment(right[start + y], ContentChangeKind.Added));
                y++;
            }
        }

        Append(segments, ContentChangeKind.Removed, left, start + x, endLeft + 1);
        Append(segments, ContentChangeKind.Added, right, start + y, endRight + 1);
    }

    private static void Append(
        List<TextSegment> segments,
        ContentChangeKind kind,
        string[] words,
        int from,
        int to)
    {
        for (var i = from; i < to; i++)
        {
            segments.Add(new TextSegment(words[i], kind));
        }
    }

    /// <summary>Joins neighbouring segments of the same kind, so a run of words is one segment.</summary>
    private static List<TextSegment> Merge(List<TextSegment> segments)
    {
        var merged = new List<TextSegment>(segments.Count);

        foreach (var segment in segments)
        {
            if (merged.Count > 0 && merged[^1].Kind == segment.Kind)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + segment.Text };

                continue;
            }

            merged.Add(segment);
        }

        return merged;
    }

    private static IReadOnlyList<TextSegment> Wholesale(string? before, string? after)
    {
        var segments = new List<TextSegment>(2);

        if (!string.IsNullOrEmpty(before)) segments.Add(new TextSegment(before, ContentChangeKind.Removed));
        if (!string.IsNullOrEmpty(after)) segments.Add(new TextSegment(after, ContentChangeKind.Added));

        return segments;
    }

    /// <summary>
    /// Splits text into words, keeping the whitespace that follows each one.
    /// </summary>
    /// <remarks>
    /// Keeping the separators attached is what lets the segments be concatenated back into the
    /// original text. A diff whose rendering has to re-insert spaces gets them wrong around
    /// punctuation and at the ends of runs.
    /// </remarks>
    private static string[] Split(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var words = new List<string>();
        var index = 0;

        while (index < text.Length)
        {
            var start = index;

            while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;

            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;

            words.Add(text[start..index]);
        }

        return [.. words];
    }
}
