using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// The recycle bin and duplication (tasks P2-08 and P2-09).
/// </summary>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class RecycleBinAndDuplicationTests(SqlServerFixture fixture)
{
    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task TheDeleteImpactStatesTheSubtreeSizeAndHowMuchOfItIsLive()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widget", cancellationToken, section.Summary.Id);
        await _bench.AddPageAsync(template, "Spec", cancellationToken, child.Summary.Id);
        await _bench.AddPageAsync(template, "About", cancellationToken);

        var publishing = _bench.Resolve<IPublishingService>();

        await publishing.PublishAsync(section.Summary.Id, acknowledgeWarnings: true, cancellationToken);
        await publishing.PublishAsync(child.Summary.Id, acknowledgeWarnings: true, cancellationToken);

        var impact = await _bench.Resolve<IRecycleBinService>().DescribeAsync(
            section.Summary.Id,
            cancellationToken);

        impact.IsSuccess.Should().BeTrue(Because(impact));
        impact.Value!.Title.Should().Be("Products", "a confirmation names the page, not its identity");
        impact.Value.DescendantCount.Should().Be(2, "the bystander at the root is not beneath it");
        impact.Value.PublishedCount.Should().Be(
            2,
            "the count includes the page itself, because it is what leaves the public site too");

        _bench.Context.ChangeTracker.Clear();

        // The whole point of asking first (acceptance criterion P6 #10). A query that deleted
        // anything on the way to counting would be worse than no confirmation at all.
        (await _bench.Context.Pages.CountAsync(cancellationToken)).Should().Be(4);
    }

    [Test]
    public async Task DeletingAPageTakesItsSubtreeAndHidesItWhileKeepingItsHistory()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widget", cancellationToken, section.Summary.Id);
        var grandchild = await _bench.AddPageAsync(template, "Spec", cancellationToken, child.Summary.Id);
        var bystander = await _bench.AddPageAsync(template, "About", cancellationToken);

        await _bench.Resolve<IPublishingService>().PublishAsync(
            child.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        var result = await _bench.Resolve<IRecycleBinService>().DeleteAsync(
            section.Summary.Id,
            cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));
        result.Value!.AffectedPageIds.Should().BeEquivalentTo(
            [section.Summary.Id, child.Summary.Id, grandchild.Summary.Id]);
        result.Value.UnpublishedCount.Should().Be(1, "one of them was live");

        _bench.Context.ChangeTracker.Clear();

        // A live child under a deleted parent is a page reachable by URL and invisible in the tree,
        // which is why a delete is subtree-aware rather than per-page (spec section 14.10).
        (await _bench.Context.Pages.CountAsync(cancellationToken)).Should().Be(1);
        (await _bench.Context.Pages.SingleAsync(cancellationToken)).Id.Should().Be(bystander.Summary.Id);

        // Everything is still there, and still retrievable — which is the whole difference between
        // a soft delete and a delete (acceptance criterion P2 #10).
        (await _bench.Context.Pages.IgnoreQueryFilters().CountAsync(cancellationToken)).Should().Be(4);

        var history = await _bench.Resolve<IVersionService>().ListAsync(child.Summary.Id, cancellationToken);
        history.Value.Should().NotBeEmpty("a deleted page keeps its version history");
    }

    [Test]
    public async Task RestoringBringsTheSubtreeBackAsDraftsAndNeverRepublishesIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widget", cancellationToken, section.Summary.Id);
        var bin = _bench.Resolve<IRecycleBinService>();

        await _bench.Resolve<IPublishingService>().PublishAsync(
            child.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        await bin.DeleteAsync(section.Summary.Id, cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        var listed = await bin.ListAsync(cancellationToken);

        // One entry for the delete somebody performed, not one per page swept up by it.
        listed.Value!.Should().HaveCount(2);
        listed.Value.Single(entry => entry.Id == section.Summary.Id).IsSubtreeRoot.Should().BeTrue();
        listed.Value.Single(entry => entry.Id == section.Summary.Id).DescendantCount.Should().Be(1);
        listed.Value.Single(entry => entry.Id == child.Summary.Id).IsSubtreeRoot.Should().BeFalse();

        var restored = await bin.RestoreAsync(section.Summary.Id, cancellationToken);

        restored.IsSuccess.Should().BeTrue(Because(restored));
        restored.Value!.AffectedPageIds.Should().HaveCount(2);

        _bench.Context.ChangeTracker.Clear();

        var pages = await _bench.Context.Pages.AsNoTracking().ToListAsync(cancellationToken);

        pages.Should().HaveCount(2);

        // A restored page comes back as a draft. Anything else puts content back on the public site
        // that nobody has looked at since it was deleted.
        pages.Should().AllSatisfy(page => page.PublishedVersionId.Should().BeNull());
        pages.Should().AllSatisfy(page => page.DraftVersionId.Should().NotBeNull());
    }

    [Test]
    public async Task APageWhoseParentIsStillDeletedComesBackAtTheRootWithAWarning()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widget", cancellationToken, section.Summary.Id);
        var bin = _bench.Resolve<IRecycleBinService>();

        await bin.DeleteAsync(section.Summary.Id, cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        var restored = await bin.RestoreAsync(child.Summary.Id, cancellationToken);

        restored.IsSuccess.Should().BeTrue(Because(restored));
        restored.Value!.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.ParentStillDeleted);

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(page => page.Id == child.Summary.Id, cancellationToken);

        // Restored somewhere reachable rather than not restored at all, and the path is rewritten
        // to match — a page whose stored path disagrees with its parent is one queries stop finding.
        stored.ParentId.Should().BeNull();
        stored.Depth.Should().Be(0);
        stored.Path.Should().Be($"/{child.Summary.Id}/");
    }

    [Test]
    public async Task APermanentDeleteIsRefusedWhileAnythingStillLinksToThePage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.PageReferenceZone("related"));

        var target = await _bench.AddPageAsync(template, "Target", cancellationToken);
        var source = await _bench.AddPageAsync(template, "Source", cancellationToken);
        var bin = _bench.Resolve<IRecycleBinService>();

        await _bench.Resolve<IDraftService>().SaveAsync(
            source.Summary.Id,
            new SaveDraftRequest(Reference(template.Key, target.Summary.Id), null),
            cancellationToken);

        await bin.DeleteAsync(target.Summary.Id, cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        var refused = await bin.PurgeAsync(target.Summary.Id, cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Conflict);
        refused.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.StillReferenced);

        // The refusal names what is in the way. "3 pages reference this" is not something an editor
        // can act on.
        refused.Diagnostics.Diagnostics[0].Message.Should().Contain(source.Summary.Id.ToString());

        // Clear the link and the same purge goes through.
        await _bench.Resolve<IDraftService>().SaveAsync(
            source.Summary.Id,
            new SaveDraftRequest(ContentPayload.CreateEmpty(template.Key, 1).ToJson(), null),
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var purged = await bin.PurgeAsync(target.Summary.Id, cancellationToken);

        purged.IsSuccess.Should().BeTrue(Because(purged));

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.Pages.IgnoreQueryFilters()
            .AnyAsync(page => page.Id == target.Summary.Id, cancellationToken))
            .Should().BeFalse();

        (await _bench.Context.PageVersions
            .AnyAsync(version => version.PageId == target.Summary.Id, cancellationToken))
            .Should().BeFalse("the versions go with it");
    }

    [Test]
    public async Task APermanentDeleteNeedsAPageInTheBinAndAnAdministrator()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        var live = await _bench.Resolve<IRecycleBinService>().PurgeAsync(page.Summary.Id, cancellationToken);
        live.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.PageNotDeleted);

        // Separate from Content.Delete on purpose: this is the one operation with no undo.
        await using var editor = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(
                CmsPermissions.ContentRead,
                CmsPermissions.ContentEdit,
                CmsPermissions.ContentDelete),
            cancellationToken);

        var theirTemplate = await editor.AddTemplateAsync(
            "landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var theirPage = await editor.AddPageAsync(theirTemplate, "Pricing", cancellationToken);

        await editor.Resolve<IRecycleBinService>().DeleteAsync(theirPage.Summary.Id, cancellationToken);
        editor.Context.ChangeTracker.Clear();

        (await editor.Resolve<IRecycleBinService>().PurgeAsync(theirPage.Summary.Id, cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);
    }

    [Test]
    public async Task AShallowDuplicateStartsAtVersionOneUnpublishedWithAFreeSlug()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Detail", cancellationToken, page.Summary.Id);

        await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Text(template.Key, "The original text"), null),
            cancellationToken);
        await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var copy = await _bench.Resolve<IDuplicationService>().DuplicateAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        copy.IsSuccess.Should().BeTrue(Because(copy));

        var detail = copy.Value!;
        detail.Summary.Title.Should().Be("Pricing (copy)");
        detail.Summary.Slug.Should().Be("pricing-copy");
        detail.Summary.PublishedVersionNumber.Should().BeNull("a copy is created unpublished");
        detail.Summary.DraftVersionNumber.Should().Be(1, "history is not copied");
        detail.ContentJson.Should().Contain("The original text");
        detail.Summary.HasChildren.Should().BeFalse("a shallow copy leaves the subtree behind");

        _bench.Context.ChangeTracker.Clear();

        var versions = await _bench.Context.PageVersions
            .AsNoTracking()
            .CountAsync(version => version.PageId == detail.Summary.Id, cancellationToken);

        versions.Should().Be(1);
        _ = child;
    }

    [Test]
    public async Task ADeepDuplicateRewritesLinksInsideTheSubtreeAndLeavesLinksOutOfItAlone()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.PageReferenceZone("related"));

        var campaign = await _bench.AddPageAsync(template, "Campaign", cancellationToken);
        var inside = await _bench.AddPageAsync(template, "Landing", cancellationToken, campaign.Summary.Id);
        var alsoInside = await _bench.AddPageAsync(template, "Thanks", cancellationToken, campaign.Summary.Id);
        var outside = await _bench.AddPageAsync(template, "Contact", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        // One link within the subtree and one out of it, which is the pair that makes the rule
        // observable at all.
        await drafts.SaveAsync(
            inside.Summary.Id,
            new SaveDraftRequest(Reference(template.Key, alsoInside.Summary.Id), null),
            cancellationToken);
        await drafts.SaveAsync(
            alsoInside.Summary.Id,
            new SaveDraftRequest(Reference(template.Key, outside.Summary.Id), null),
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var copy = await _bench.Resolve<IDuplicationService>().DuplicateAsync(
            campaign.Summary.Id,
            deep: true,
            cancellationToken: cancellationToken);

        copy.IsSuccess.Should().BeTrue(Because(copy));

        _bench.Context.ChangeTracker.Clear();

        var copies = await _bench.Context.Pages
            .AsNoTracking()
            .Include(page => page.DraftVersion)
            .Where(page => page.Path.StartsWith(
                _bench.Context.Pages.Where(root => root.Id == copy.Value!.Summary.Id)
                    .Select(root => root.Path).First()))
            .ToListAsync(cancellationToken);

        copies.Should().HaveCount(3, "the root and both children were copied");

        var copiedLanding = copies.Single(page => page.DraftVersion!.Title == "Landing");
        var copiedThanks = copies.Single(page => page.DraftVersion!.Title == "Thanks");

        // The link between the two copied pages now points at the copy. Without this, "duplicate
        // last year's campaign" produces a section whose every internal link goes back to last year.
        copiedLanding.DraftVersion!.ContentJson.Should()
            .Contain($"\"value\":{copiedThanks.Id}")
            .And.NotContain($"\"value\":{alsoInside.Summary.Id}");

        // The link out of the subtree is left where it was, because the page it names was not copied.
        copiedThanks.DraftVersion!.ContentJson.Should().Contain($"\"value\":{outside.Summary.Id}");

        var rows = await _bench.Context.ContentReferences
            .AsNoTracking()
            .Where(row => row.SourceVersionId == copiedLanding.DraftVersionId)
            .ToListAsync(cancellationToken);

        rows.Should().ContainSingle().Which.TargetId.Should().Be(copiedThanks.Id);
    }

    [Test]
    public async Task ASectionCannotBeDuplicatedIntoItself()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widget", cancellationToken, section.Summary.Id);

        var result = await _bench.Resolve<IDuplicationService>().DuplicateAsync(
            section.Summary.Id,
            deep: true,
            child.Summary.Id,
            cancellationToken);

        result.Outcome.Should().Be(CmsOutcome.Invalid);
        result.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.ParentNotFound);
    }

    [Test]
    public async Task DuplicatingTwiceProducesTwoDistinctSlugs()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var duplication = _bench.Resolve<IDuplicationService>();

        var first = await duplication.DuplicateAsync(page.Summary.Id, cancellationToken: cancellationToken);
        _bench.Context.ChangeTracker.Clear();
        var second = await duplication.DuplicateAsync(page.Summary.Id, cancellationToken: cancellationToken);

        first.Value!.Summary.Slug.Should().Be("pricing-copy");
        second.Value!.Summary.Slug.Should().Be("pricing-copy-2");
    }

    private static string Text(string templateKey, string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
          "zones": { "hero": { "type": "plainText", "value": "{{text}}" } } }
        """;

    private static string Reference(string templateKey, int pageId) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
          "zones": { "related": { "type": "pageReference", "value": {{pageId}} } } }
        """;

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
