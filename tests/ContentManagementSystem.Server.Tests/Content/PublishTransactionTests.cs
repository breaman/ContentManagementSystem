using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// Forces a failure at each step of a publish and asserts that none of it survived (task P2-12).
/// </summary>
/// <remarks>
/// Risk R4, and the reason it is stop-the-line severity: every partial outcome of a publish is a
/// state with no repair path that does not begin with somebody noticing. A version marked
/// <c>Published</c> that no page points at is invisible; a page repointed at a version whose
/// reference rows were never written is live and unreachable by cache invalidation.
/// <para>
/// The fault is injected through an EF interceptor that throws on the nth <c>SaveChanges</c> rather
/// than through a seam in the service. A production hook that exists only for a test is a hook a
/// deployment can trip over, and it would also prove less: the interceptor fails at the real
/// database boundary, inside the real transaction.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PublishTransactionTests(SqlServerFixture fixture)
{
    private FailingSaveInterceptor _interceptor = null!;
    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync()
    {
        _interceptor = new FailingSaveInterceptor();
        _bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: TestContext.Current!.Execution.CancellationToken,
            interceptor: _interceptor);
    }

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    /// <summary>
    /// One case per <c>SaveChanges</c> inside the publish transaction.
    /// </summary>
    /// <remarks>
    /// The steps are, in order: insert the new version; archive the previous one and repoint the
    /// page; project the reference rows; and the placeholder for the cache-invalidation outbox row
    /// that arrives in P8. Failing at any of them has to leave the page exactly as it was.
    /// </remarks>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task AFailureAtAnyStepOfAPublishLeavesNothingBehind(int failingStep)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.TextZone("hero"));

        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();
        var publishing = _bench.Resolve<IPublishingService>();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, "First"), null),
            cancellationToken);

        // A first successful publish, so the failing one has a previous version to archive and a
        // page pointer to move. Failing on a page that was never live would not exercise step 2.
        var live = await publishing.PublishAsync(page.Summary.Id, cancellationToken: cancellationToken);
        live.IsSuccess.Should().BeTrue();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, "Second"), null),
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var before = await SnapshotAsync(page.Summary.Id, cancellationToken);

        _interceptor.FailOnCall(failingStep);

        var attempt = async () => await publishing.PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        await attempt.Should().ThrowAsync<Exception>(
            "a publish that cannot complete must fail loudly rather than commit half of itself");

        _interceptor.Reset();
        _bench.Context.ChangeTracker.Clear();

        var after = await SnapshotAsync(page.Summary.Id, cancellationToken);

        after.Should().BeEquivalentTo(before, "the whole publish rolls back or none of it does");
    }

    [Test]
    public async Task APublishThatSucceedsLeavesEveryStepApplied()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.PageReferenceZone("related"));

        var target = await _bench.AddPageAsync(template, "Target", cancellationToken);
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(
                $$"""
                { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
                  "zones": { "related": { "type": "pageReference", "value": {{target.Summary.Id}} } } }
                """,
                null),
            cancellationToken);

        var result = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        result.IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        // The control case for the theory above. Without it, an interceptor that broke publishing
        // outright would make every roll-back assertion pass for the wrong reason.
        var after = await SnapshotAsync(page.Summary.Id, cancellationToken);

        after.PublishedVersionId.Should().Be(result.Value!.VersionId);
        after.Versions.Should().HaveCount(2);
        after.ReferenceRows.Should().Be(2, "the draft and the published version each project a row");
    }

    /// <summary>Everything a publish touches, in one comparable value.</summary>
    private async Task<PageSnapshot> SnapshotAsync(int pageId, CancellationToken cancellationToken)
    {
        var page = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == pageId, cancellationToken);

        var versions = await _bench.Context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == pageId)
            .OrderBy(version => version.VersionNumber)
            .Select(version => new VersionSnapshot(
                version.Id,
                version.VersionNumber,
                version.Status,
                version.ContentJson))
            .ToListAsync(cancellationToken);

        var versionIds = versions.Select(version => version.Id).ToList();

        var referenceRows = await _bench.Context.ContentReferences
            .AsNoTracking()
            .CountAsync(row => versionIds.Contains(row.SourceVersionId), cancellationToken);

        return new PageSnapshot(page.DraftVersionId, page.PublishedVersionId, versions, referenceRows);
    }

    private static string Payload(string templateKey, string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
          "zones": { "hero": { "type": "plainText", "value": "{{text}}" } } }
        """;

    private sealed record VersionSnapshot(int Id, int VersionNumber, PageVersionStatus Status, string ContentJson);

    private sealed record PageSnapshot(
        int? DraftVersionId,
        int? PublishedVersionId,
        IReadOnlyList<VersionSnapshot> Versions,
        int ReferenceRows);
}

/// <summary>
/// Throws on a chosen <c>SaveChanges</c>, and otherwise gets out of the way.
/// </summary>
/// <remarks>
/// Counting is armed explicitly rather than running always, so the arrange half of a test — which
/// saves plenty of times of its own — cannot consume the budget before the operation under test
/// begins.
/// </remarks>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    private int _failOnCall;
    private int _calls;

    /// <summary>Arms the interceptor to throw on the nth save from now.</summary>
    /// <param name="call">Which save to fail, counting from one.</param>
    public void FailOnCall(int call)
    {
        _failOnCall = call;
        _calls = 0;
    }

    /// <summary>Disarms it, so the assertions afterwards can query normally.</summary>
    public void Reset() => _failOnCall = 0;

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_failOnCall > 0 && ++_calls == _failOnCall)
        {
            throw new InvalidOperationException(
                $"Injected failure at publish step {_failOnCall}.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
