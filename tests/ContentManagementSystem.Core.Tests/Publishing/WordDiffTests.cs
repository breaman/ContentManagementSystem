using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Core.Tests.Publishing;

/// <summary>
/// Word-level text comparison (task P2-14, spec section 11.4).
/// </summary>
/// <remarks>
/// The invariant every case here checks, beyond whatever it is about: concatenating the segments
/// that survive gives back the original text on each side. A diff that renders correctly and cannot
/// be reassembled is one that has quietly dropped a word, and the reader has no way to tell.
/// </remarks>
public class WordDiffTests
{
    [Fact]
    public void IdenticalTextProducesOneUnchangedRun()
    {
        var segments = WordDiff.Compute("the hero headline is fine", "the hero headline is fine");

        segments.Should().ContainSingle()
            .Which.Kind.Should().Be(ContentChangeKind.Unchanged);
    }

    [Fact]
    public void AReplacedWordIsReportedAsARemovalFollowedByAnAddition()
    {
        var segments = WordDiff.Compute("the hero headline is wrong", "the hero headline is right");

        Rebuild(segments, ContentChangeKind.Removed).Should().Be("the hero headline is wrong");
        Rebuild(segments, ContentChangeKind.Added).Should().Be("the hero headline is right");

        // Everything up to the changed word survives as one run, which is what makes inline
        // highlighting show a changed word rather than a changed paragraph.
        segments[0].Kind.Should().Be(ContentChangeKind.Unchanged);
        segments[0].Text.Should().Be("the hero headline is ");

        // Removals come before additions at the same position, so a side-by-side view can lay the
        // two columns out from one list.
        segments[1].Kind.Should().Be(ContentChangeKind.Removed);
        segments[2].Kind.Should().Be(ContentChangeKind.Added);
    }

    [Fact]
    public void InsertedWordsAreTheOnlyThingReportedAsAdded()
    {
        var segments = WordDiff.Compute("plans for teams", "plans and pricing for teams");

        segments.Where(segment => segment.Kind is ContentChangeKind.Removed).Should().BeEmpty();
        Rebuild(segments, ContentChangeKind.Added).Should().Be("plans and pricing for teams");
    }

    [Fact]
    public void TextAppearingOrDisappearingEntirelyIsOneSegment()
    {
        WordDiff.Compute(null, "brand new").Should().ContainSingle()
            .Which.Kind.Should().Be(ContentChangeKind.Added);

        WordDiff.Compute("all gone", null).Should().ContainSingle()
            .Which.Kind.Should().Be(ContentChangeKind.Removed);

        WordDiff.Compute(null, null).Should().BeEmpty();
    }

    [Fact]
    public void AReorderedSentenceKeepsTheWordsItStillHas()
    {
        var segments = WordDiff.Compute("alpha beta gamma delta", "alpha gamma beta delta");

        Rebuild(segments, ContentChangeKind.Removed).Should().Be("alpha beta gamma delta");
        Rebuild(segments, ContentChangeKind.Added).Should().Be("alpha gamma beta delta");

        // The longest common subsequence keeps three of the four words in place rather than
        // reporting the whole string replaced.
        segments.Count(segment => segment.Kind is ContentChangeKind.Unchanged).Should().BeGreaterThan(1);
    }

    [Fact]
    public void TextTooLongToCompareWordByWordDegradesToAWholesaleReplacement()
    {
        var before = string.Join(' ', Enumerable.Range(0, WordDiff.MaxWords + 1).Select(i => $"w{i}"));
        var after = before + " and one more";

        var segments = WordDiff.Compute(before, after);

        // Still correct, just less useful — which is the right trade against a quadratic comparison
        // tying up a request thread on a pasted book.
        segments.Should().HaveCount(2);
        segments[0].Kind.Should().Be(ContentChangeKind.Removed);
        segments[1].Kind.Should().Be(ContentChangeKind.Added);
    }

    [Fact]
    public void WhitespaceIsCarriedWithTheWordsSoTheTextCanBeReassembled()
    {
        const string Before = "one  two\tthree\nfour";
        const string After = "one  two\tthree\nfive";

        var segments = WordDiff.Compute(Before, After);

        // Not cosmetic: a renderer that had to re-insert the spaces would get them wrong around
        // punctuation and at the ends of runs.
        Rebuild(segments, ContentChangeKind.Removed).Should().Be(Before);
        Rebuild(segments, ContentChangeKind.Added).Should().Be(After);
    }

    /// <summary>Reassembles one side of the diff from the segments that belong to it.</summary>
    private static string Rebuild(IReadOnlyList<TextSegment> segments, ContentChangeKind side) =>
        string.Concat(segments
            .Where(segment => segment.Kind is ContentChangeKind.Unchanged || segment.Kind == side)
            .Select(segment => segment.Text));
}
