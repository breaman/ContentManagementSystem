using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// Version history, rollback, retention, and the diff (tasks P2-13 and P2-14).
/// </summary>
[Collection(SqlServerCollectionNames.SqlServer)]
public class VersionAndDiffTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string BlockA = "11111111-1111-4111-8111-111111111111";
    private const string BlockB = "22222222-2222-4222-8222-222222222222";
    private const string BlockC = "33333333-3333-4333-8333-333333333333";

    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task HistoryListsEveryVersionWithItsStatusAuthorAndTimestamp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (template, page) = await PublishedPageAsync(cancellationToken);

        var history = await _bench.Resolve<IVersionService>().ListAsync(page.Summary.Id, cancellationToken);

        history.IsSuccess.Should().BeTrue();

        var versions = history.Value!;
        versions.Should().HaveCount(2);

        // Newest first, which is the order the editor's panel reads in.
        versions[0].VersionNumber.Should().Be(2);
        versions[0].IsPublished.Should().BeTrue();
        versions[0].Status.Should().Be(nameof(PageVersionStatus.Published));
        versions[0].PublishedOn.Should().NotBeNull();
        versions[0].PublishedBy.Should().Be(_bench.Users.UserId);

        versions[1].IsDraft.Should().BeTrue();
        versions[1].CreatedOn.Should().NotBeNull();
        versions.Should().AllSatisfy(version => version.Title.Should().NotBeNullOrWhiteSpace());

        _ = template;
    }

    [Fact]
    public async Task RestoringAVersionCopiesItIntoTheDraftAndLeavesThePublishedVersionAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (template, page) = await PublishedPageAsync(cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Text(template.Key, "A later experiment"), null),
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == page.Summary.Id, cancellationToken);

        var publishedId = stored.PublishedVersionId!.Value;
        var publishedBytes = await _bench.Context.PageVersions
            .AsNoTracking()
            .Where(version => version.Id == publishedId)
            .Select(version => version.ContentJson)
            .SingleAsync(cancellationToken);

        var restored = await _bench.Resolve<IVersionService>().RestoreAsync(
            page.Summary.Id,
            publishedId,
            cancellationToken);

        restored.IsSuccess.Should().BeTrue(Because(restored));
        restored.Value!.ContentJson.Should().Be(publishedBytes);
        restored.Value.VersionNumber.Should().Be(1, "a restore copies into the draft's own row");

        _bench.Context.ChangeTracker.Clear();

        // The timeline stays forward-moving: the published row is not resurrected, it is copied
        // from, and nothing about it changed (acceptance criterion P2 #7, spec section 11.5).
        var after = await _bench.Context.PageVersions
            .AsNoTracking()
            .SingleAsync(version => version.Id == publishedId, cancellationToken);

        after.ContentJson.Should().Be(publishedBytes);
        after.Status.Should().Be(PageVersionStatus.Published);

        var pageAfter = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == page.Summary.Id, cancellationToken);

        pageAfter.PublishedVersionId.Should().Be(publishedId);
    }

    [Fact]
    public async Task AVersionBelongingToAnotherPageIsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, first) = await PublishedPageAsync(cancellationToken);
        var (_, second) = await PublishedPageAsync(cancellationToken, "Other");

        var versions = _bench.Resolve<IVersionService>();
        var foreign = (await versions.ListAsync(second.Summary.Id, cancellationToken)).Value![0].Id;

        (await versions.GetAsync(first.Summary.Id, foreign, cancellationToken))
            .Outcome.Should().Be(CmsOutcome.NotFound);

        (await versions.RestoreAsync(first.Summary.Id, foreign, cancellationToken))
            .Outcome.Should().Be(CmsOutcome.NotFound);
    }

    [Fact]
    public async Task RetentionKeepsWhatAnEditorWouldBeUpsetToLose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (template, page) = await PublishedPageAsync(cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        await drafts.CheckpointAsync(page.Summary.Id, "before the rewrite", cancellationToken);

        // Enough ordinary versions to push past the per-page ceiling once the window has elapsed.
        for (var i = 0; i < VersionService.KeepPerPage + 5; i++)
        {
            await drafts.SaveAsync(
                page.Summary.Id,
                new SaveDraftRequest(Text(template.Key, $"iteration {i}"), null),
                cancellationToken);
            await drafts.CheckpointAsync(page.Summary.Id, $"auto {i}", cancellationToken);
        }

        _bench.Context.ChangeTracker.Clear();

        // Strip the labels off most of them, so the sample has ordinary versions to prune as well as
        // the ones the policy protects.
        var ordinary = await _bench.Context.PageVersions
            .Where(version => version.PageId == page.Summary.Id &&
                version.Status == PageVersionStatus.Archived &&
                version.Label!.StartsWith("auto"))
            .ToListAsync(cancellationToken);

        foreach (var version in ordinary)
        {
            version.Label = null;
        }

        await _bench.Context.SaveChangesAsync(cancellationToken);

        var before = await _bench.Context.PageVersions
            .AsNoTracking()
            .CountAsync(version => version.PageId == page.Summary.Id, cancellationToken);

        // Everything inside the retention window survives regardless of count, which is why the
        // clock has to move before anything is prunable at all.
        var untouched = await _bench.Resolve<IVersionService>().PruneAsync(cancellationToken);
        untouched.VersionsRemoved.Should().Be(0, "nothing is older than the retention window yet");

        _bench.Clock.Advance(TimeSpan.FromDays(VersionService.DefaultRetentionDays + 1));

        var swept = await _bench.Resolve<IVersionService>().PruneAsync(cancellationToken);

        swept.VersionsRemoved.Should().BeGreaterThan(0);

        _bench.Context.ChangeTracker.Clear();

        var kept = await _bench.Context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == page.Summary.Id)
            .ToListAsync(cancellationToken);

        kept.Should().HaveCountLessThan(before);
        kept.Should().Contain(version => version.Status == PageVersionStatus.Draft);
        kept.Should().Contain(version => version.Status == PageVersionStatus.Published);
        kept.Should().Contain(version => version.Label == "before the rewrite");
    }

    [Fact]
    public async Task NothingIsPrunedFromAPageInTheRecycleBin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (template, page) = await PublishedPageAsync(cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        for (var i = 0; i < VersionService.KeepPerPage + 5; i++)
        {
            await drafts.SaveAsync(
                page.Summary.Id,
                new SaveDraftRequest(Text(template.Key, $"iteration {i}"), null),
                cancellationToken);
            await drafts.CheckpointAsync(page.Summary.Id, $"auto {i}", cancellationToken);
        }

        _bench.Context.ChangeTracker.Clear();

        foreach (var version in await _bench.Context.PageVersions
            .Where(version => version.PageId == page.Summary.Id && version.Label!.StartsWith("auto"))
            .ToListAsync(cancellationToken))
        {
            version.Label = null;
        }

        await _bench.Context.SaveChangesAsync(cancellationToken);
        await _bench.Resolve<IRecycleBinService>().DeleteAsync(page.Summary.Id, cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        var before = await _bench.Context.PageVersions
            .AsNoTracking()
            .CountAsync(version => version.PageId == page.Summary.Id, cancellationToken);

        _bench.Clock.Advance(TimeSpan.FromDays(VersionService.DefaultRetentionDays + 1));

        await _bench.Resolve<IVersionService>().PruneAsync(cancellationToken);

        var after = await _bench.Context.PageVersions
            .AsNoTracking()
            .CountAsync(version => version.PageId == page.Summary.Id, cancellationToken);

        // A restore that came back with no history is not a restore (spec section 11.7).
        after.Should().Be(before);
    }

    [Fact]
    public async Task AReorderedBlockIsReportedAsMovedRatherThanRemovedAndAdded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.BlocksZone("body"));

        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Blocks(template.Key, BlockA, BlockB, BlockC), null),
            cancellationToken);

        var first = await _bench.Resolve<IDraftService>().CheckpointAsync(
            page.Summary.Id, "before", cancellationToken);

        first.IsSuccess.Should().BeTrue();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Blocks(template.Key, BlockC, BlockA, BlockB), null),
            cancellationToken);

        var versions = (await _bench.Resolve<IVersionService>().ListAsync(page.Summary.Id, cancellationToken)).Value!;
        var before = versions.Single(version => version.Label == "before");
        var draft = versions.Single(version => version.IsDraft);

        var diff = await _bench.Resolve<IContentDiffService>().CompareAsync(
            page.Summary.Id,
            before.Id,
            draft.Id,
            cancellationToken);

        diff.IsSuccess.Should().BeTrue(Because(diff));

        var zone = diff.Value!.Zones.Should().ContainSingle().Subject;
        zone.ZoneKey.Should().Be("body");

        // Acceptance criterion P2 #6. A positional comparison reports the whole array as different,
        // which is exactly useless on the edit people make most.
        zone.Blocks.Should().AllSatisfy(block =>
            block.Kind.Should().Be(ContentChangeKind.Moved));

        zone.Blocks.Should().Contain(block =>
            block.BlockId == Guid.Parse(BlockC) && block.BeforeIndex == 2 && block.AfterIndex == 0);
    }

    [Fact]
    public async Task AChangedBlockPropertyIsDiffedWordByWordAndAnUntouchedBlockIsSilent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.BlocksZone("body"));

        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Blocks(template.Key, BlockA, BlockB), null),
            cancellationToken);
        await drafts.CheckpointAsync(page.Summary.Id, "before", cancellationToken);

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(
                Blocks(template.Key, BlockA, BlockB).Replace(
                    $$"""{ "type": "plainText", "value": "text {{BlockB}}" }""",
                    """{ "type": "plainText", "value": "text rewritten entirely" }""",
                    StringComparison.Ordinal),
                null),
            cancellationToken);

        var versions = (await _bench.Resolve<IVersionService>().ListAsync(page.Summary.Id, cancellationToken)).Value!;

        var diff = await _bench.Resolve<IContentDiffService>().CompareAsync(
            page.Summary.Id,
            versions.Single(version => version.Label == "before").Id,
            versions.Single(version => version.IsDraft).Id,
            cancellationToken);

        var zone = diff.Value!.Zones.Should().ContainSingle().Subject;

        // Only the block that changed is reported. A diff that lists every block on the page as
        // "changed" is one nobody reads.
        var block = zone.Blocks.Should().ContainSingle().Subject;
        block.BlockId.Should().Be(Guid.Parse(BlockB));
        block.Kind.Should().Be(ContentChangeKind.Changed);

        var property = block.Properties.Should().ContainSingle().Subject;
        property.Key.Should().Be("heading");
        property.Segments.Should().Contain(segment => segment.Kind == ContentChangeKind.Added);
        property.Segments.Should().Contain(segment => segment.Kind == ContentChangeKind.Removed);
    }

    [Fact]
    public async Task MetadataIsDiffedAsAFlatListAndAnIdenticalPairReportsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (template, page) = await PublishedPageAsync(cancellationToken);
        var diffs = _bench.Resolve<IContentDiffService>();
        var versions = (await _bench.Resolve<IVersionService>().ListAsync(page.Summary.Id, cancellationToken)).Value!;
        var published = versions.Single(version => version.IsPublished);
        var draft = versions.Single(version => version.IsDraft);

        var unchanged = await diffs.CompareAsync(page.Summary.Id, published.Id, published.Id, cancellationToken);
        unchanged.Value!.HasChanges.Should().BeFalse("a version is identical to itself");

        await _bench.Resolve<IPageService>().PatchMetadataAsync(
            page.Summary.Id,
            new PatchPageMetadataRequest { Title = "Pricing and Plans", MetaDescription = "What it costs." },
            cancellationToken: cancellationToken);

        var changed = await diffs.CompareAsync(page.Summary.Id, published.Id, draft.Id, cancellationToken);

        changed.Value!.Metadata.Should().Contain(change =>
            change.Name == nameof(PageVersion.Title) && change.After == "Pricing and Plans");
        changed.Value.Metadata.Should().Contain(change =>
            change.Name == nameof(PageVersion.MetaDescription) && change.After == "What it costs.");

        _ = template;
    }

    /// <summary>Creates a page, fills its text zone, and publishes it.</summary>
    private async Task<(Template Template, PageDetail Page)> PublishedPageAsync(
        CancellationToken cancellationToken,
        string title = "Pricing")
    {
        var template = await _bench.Context.Templates
            .FirstOrDefaultAsync(candidate => candidate.Key == "landing", cancellationToken)
            ?? await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));

        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Text(template.Key, "The live text"), null),
            cancellationToken);

        var publish = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        publish.IsSuccess.Should().BeTrue(Because(publish));

        return (template, page);
    }

    private static string Text(string templateKey, string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
          "zones": { "hero": { "type": "plainText", "value": "{{text}}" } } }
        """;

    private static string Blocks(string templateKey, params string[] blockIds)
    {
        var items = string.Join(",\n", blockIds.Select(id =>
            $$"""
            { "id": "{{id}}", "blockTypeKey": "rawHtml", "blockTypeRevision": 1,
              "properties": { "heading": { "type": "plainText", "value": "text {{id}}" } } }
            """));

        return $$"""
        { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
          "zones": { "body": { "type": "blocks", "items": [ {{items}} ] } } }
        """;
    }

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
