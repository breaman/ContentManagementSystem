using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Tests.Publishing;

/// <summary>
/// How a page's next version number is chosen (task P2-24, spec section 11.3).
/// </summary>
/// <remarks>
/// Small enough to look self-evident, which is why it is worth pinning: every wrong version of this
/// rule — the count plus one, the number of the newest row plus one, an incrementing field on the
/// page — is correct until history is pruned or a version is deleted, and then quietly reissues a
/// number that already means something else.
/// </remarks>
public class VersionNumbersTests
{
    [Test]
    public void APageWithNoVersionsStartsAtOne()
    {
        VersionNumbers.Next([]).Should().Be(VersionNumbers.First);
        VersionNumbers.First.Should().Be(1);
    }

    [Test]
    public void TheNextNumberIsTheHighestEverIssuedPlusOne()
    {
        VersionNumbers.Next([1, 2, 3]).Should().Be(4);

        // Not the count plus one. A history with a gap in it is the normal state of a long-lived
        // page, and counting rows would hand out 4 here — a number version 4 already had.
        VersionNumbers.Next([1, 4, 5]).Should().Be(6);
    }

    [Test]
    public void TheOrderTheNumbersArriveInDoesNotMatter()
    {
        // The query feeding this is ordered by version number descending today. A rule that read the
        // first element would work perfectly until somebody reordered it for the retention sweep.
        VersionNumbers.Next([5, 1, 3]).Should().Be(6);
        VersionNumbers.Next([3, 1, 5]).Should().Be(6);
    }

    [Test]
    public void PruningTheOldHistoryDoesNotMakeANumberAvailableAgain()
    {
        var history = Enumerable.Range(1, 30).ToList();
        var next = VersionNumbers.Next(history);

        // What a retention sweep would leave behind: everything below the newest twenty is gone.
        var pruned = history.Where(number => number > 10).ToList();

        // The two clauses have to agree, and this is where they meet. RetentionPolicy keeps the most
        // recent KeepPerPage versions by rank, so the highest number always survives — and the whole
        // reason numbering may read the maximum rather than a monotonic counter is that guarantee.
        VersionNumbers.Next(pruned).Should().Be(next);
    }

    [Test]
    public void TheNewestVersionIsNeverThePrunableOne()
    {
        var cutoff = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        var ancient = cutoff.AddYears(-2);

        // Rank 1 is the newest row. Even with nothing else protecting it — never published, never
        // labelled, far outside the window — it is kept, which is what the assertion above rests on.
        var newest = new RetentionCandidate(
            Id: 900,
            Rank: 1,
            Status: PageVersionStatus.Archived,
            Label: null,
            PublishedOn: null,
            CreatedOn: ancient,
            IsPointedAt: false);

        RetentionPolicy.Decide(newest, cutoff).Should().Be(RetentionReason.RecentlyEnough);
    }
}
