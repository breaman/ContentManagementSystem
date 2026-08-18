using ContentManagementSystem.Core.Structure;

namespace ContentManagementSystem.Core.Tests.Structure;

/// <summary>
/// What a sync report means, which is what the CLI's exit codes are computed from (task P1-28).
/// </summary>
/// <remarks>
/// Worth its own tests because <c>cms schema diff</c> is a CI gate: these two properties decide
/// whether a build goes red, and getting either backwards produces a check that either never fires
/// or never passes.
/// </remarks>
public class SchemaSyncReportTests
{
    [Test]
    public void AnEmptyReportHasNothingToDoAndNothingWrong()
    {
        SchemaSyncReport.Empty.HasPendingWork.Should().BeFalse();
        SchemaSyncReport.Empty.HasProblems.Should().BeFalse();
    }

    [Test]
    [Arguments(SchemaChangeKind.Created)]
    [Arguments(SchemaChangeKind.SlotAdded)]
    [Arguments(SchemaChangeKind.SlotUpdated)]
    public void AChangeThatWouldTouchTheDatabaseIsPendingWork(SchemaChangeKind kind)
    {
        Report(kind).HasPendingWork.Should().BeTrue();
    }

    [Test]
    public void AKeptUnlistedSlotIsNotPendingWork()
    {
        // It describes what the sync is deliberately *not* doing. A deployment whose only finding is
        // a zone the files do not mention is in sync, and failing CI for it would make the check
        // impossible to keep green while anyone edits structure in the backoffice.
        var report = Report(SchemaChangeKind.KeptUnlisted);

        report.HasPendingWork.Should().BeFalse();
        report.HasProblems.Should().BeFalse();
    }

    [Test]
    public void ARefusalIsAProblemButNotPendingWork()
    {
        var report = Report(SchemaChangeKind.Refused);

        // Nothing will be written for it, ever — so it is not work waiting to happen. It still fails
        // the drift check, because a file asking for something that can never be applied should be
        // taken out of the repository rather than reported on every future run.
        report.HasPendingWork.Should().BeFalse();
        report.HasProblems.Should().BeTrue();
    }

    [Test]
    public void AnUnreadableFileIsAProblem()
    {
        new SchemaSyncReport([], 1, ["template.broken.json: unexpected character"])
            .HasProblems.Should().BeTrue();
    }

    private static SchemaSyncReport Report(SchemaChangeKind kind) =>
        new([new SchemaChange(SchemaKind.Template, "landing", kind, "detail")], 1, []);
}
