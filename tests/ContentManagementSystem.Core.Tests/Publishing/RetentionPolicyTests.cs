using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Tests.Publishing;

/// <summary>
/// Which version a retention sweep may destroy, and which clause spares it
/// (task P2-24, spec section 11.7).
/// </summary>
/// <remarks>
/// Every case here is a permanent, silent data loss if the rule is wrong: the sweep runs nightly,
/// unattended, and a version it removed is not recoverable from anywhere in the application.
/// <c>VersionAndDiffTests</c> proves the sweep as a whole against a real database; this proves each
/// clause on its own, which that suite cannot do — arranging a version that is protected by exactly
/// one clause and no other takes ninety days of history per case.
/// </remarks>
public class RetentionPolicyTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Old enough, and far enough down the list, to be prunable on its own merits.</summary>
    private static RetentionCandidate Ordinary => new(
        Id: 42,
        Rank: RetentionPolicy.KeepPerPage + 1,
        Status: PageVersionStatus.Archived,
        Label: null,
        PublishedOn: null,
        CreatedOn: Cutoff.AddDays(-1),
        IsPointedAt: false);

    [Test]
    public void AnOrdinaryOldVersionOutsideTheRecentSetIsPrunable()
    {
        // The control. Without it, every assertion below passes for a policy that keeps everything,
        // which is a policy that quietly does nothing at all.
        RetentionPolicy.Decide(Ordinary, Cutoff).Should().Be(RetentionReason.Prunable);
    }

    [Test]
    public void TheDraftAndThePublishedVersionAreKept()
    {
        // Destroying one of these does not lose history, it breaks the page: Page.DraftVersionId
        // and PublishedVersionId point at them, and nothing serves a page whose pointer dangles.
        RetentionPolicy.Decide(Ordinary with { IsPointedAt = true }, Cutoff)
            .Should().Be(RetentionReason.Pointer);
    }

    [Test]
    public void AVersionThatWasEverLiveIsKept()
    {
        // Both spellings, because they part company. A superseded publish is Archived and keeps its
        // PublishedOn stamp, so reading only the status would prune the entire published history the
        // moment a page was published twice — the rows a rollback exists to go back to.
        RetentionPolicy.Decide(Ordinary with { PublishedOn = Cutoff.AddYears(-1) }, Cutoff)
            .Should().Be(RetentionReason.Published);

        RetentionPolicy.Decide(Ordinary with { Status = PageVersionStatus.Published }, Cutoff)
            .Should().Be(RetentionReason.Published);
    }

    [Test]
    public void ANamedCheckpointIsKept()
    {
        RetentionPolicy.Decide(Ordinary with { Label = "before the rewrite" }, Cutoff)
            .Should().Be(RetentionReason.Checkpoint);

        // A label of blanks is not a name. Treating it as one would let an empty string from a form
        // pin a page's whole history in place for ever.
        RetentionPolicy.Decide(Ordinary with { Label = "   " }, Cutoff)
            .Should().Be(RetentionReason.Prunable);
    }

    [Test]
    public void AVersionInsideTheRetentionWindowIsKeptHoweverFarDownTheListItIs()
    {
        RetentionPolicy.Decide(Ordinary with { CreatedOn = Cutoff }, Cutoff)
            .Should().Be(RetentionReason.InsideWindow, "the cutoff itself is inside the window");

        RetentionPolicy.Decide(Ordinary with { CreatedOn = Cutoff.AddSeconds(1) }, Cutoff)
            .Should().Be(RetentionReason.InsideWindow);

        RetentionPolicy.Decide(Ordinary with { CreatedOn = Cutoff.AddSeconds(-1) }, Cutoff)
            .Should().Be(RetentionReason.Prunable);
    }

    [Test]
    public void AVersionWhoseAgeIsUnknownIsKept()
    {
        // CreatedOn is written by the audit interceptor. Its absence means the row's age cannot be
        // established, which is not a licence to delete it — the fallback has to fail safe.
        RetentionPolicy.Decide(Ordinary with { CreatedOn = null }, Cutoff)
            .Should().Be(RetentionReason.InsideWindow);
    }

    [Test]
    [Arguments(1, RetentionReason.RecentlyEnough)]
    [Arguments(RetentionPolicy.KeepPerPage, RetentionReason.RecentlyEnough)]
    [Arguments(RetentionPolicy.KeepPerPage + 1, RetentionReason.Prunable)]
    public void TheMostRecentVersionsSurviveTheWindowByCount(int rank, RetentionReason expected)
    {
        // The boundary in both directions. Off by one here is either twenty-one versions kept for
        // ever or the twentieth destroyed, and neither is visible until months of history exist.
        RetentionPolicy.Decide(Ordinary with { Rank = rank }, Cutoff).Should().Be(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments(0)]
    [Arguments(-30)]
    public void AnUnsetOrNonsensicalWindowFallsBackToTheDefault(int? configured)
    {
        // The seeded SiteSettings row carries zero. Reading that literally as "keep nothing" would
        // make a fresh deployment's first nightly sweep the most destructive one it ever runs.
        RetentionPolicy.WindowDays(configured).Should().Be(RetentionPolicy.DefaultRetentionDays);
    }

    [Test]
    public void AConfiguredWindowIsHonoured()
    {
        RetentionPolicy.WindowDays(30).Should().Be(30);

        var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

        RetentionPolicy.CutoffFrom(now, 30).Should().Be(now.AddDays(-30));
        RetentionPolicy.CutoffFrom(now, null).Should().Be(now.AddDays(-RetentionPolicy.DefaultRetentionDays));
    }

    [Test]
    public void TheDefaultsAreTheOnesTheSpecNames()
    {
        // Pinned rather than derived. These two numbers are a published promise about how much
        // history a site keeps, and changing either is a decision rather than a tidy-up.
        RetentionPolicy.KeepPerPage.Should().Be(20);
        RetentionPolicy.DefaultRetentionDays.Should().Be(90);
    }
}
