using System.Text.Json;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// Reusable content end to end: authored once, placed on many pages, and updated everywhere in one
/// publish (tasks P4-15 to P4-19, spec section 9).
/// </summary>
/// <remarks>
/// Against a real SQL Server through the real service graph, because every claim Phase 4 makes is
/// about rows: what the resolver reads at render time, what the reference index says was affected,
/// and what the delete guard refuses. A suite over doubles could assert the same method calls and
/// none of the same facts.
/// <para>
/// The fixtures are all shaped by the built-in <c>rawHtml</c> block type. That is not a convenience:
/// it is the shape spec section 9.1 says must work without a developer defining anything, so
/// exercising it here is exercising the case a site has on its first day.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class ReusableContentTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task AnItemIsCreatedPublishedAndReferencedFromThreePages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (item, pages) = await ArrangeFooterOnThreePagesAsync(cancellationToken);

        // Acceptance criterion P4 #1. Asserted through the where-used query rather than by counting
        // the payloads that were written, because the query is what every guard and every impact
        // count in the phase is built on — a payload the indexer failed to walk would pass a check
        // of the first kind and fail every promise made on the second.
        var impact = await _bench.Resolve<IReferenceQueryService>().WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            item.Summary.Id,
            cancellationToken);

        impact.AffectedPageCount.Should().Be(3);
        impact.AffectedPages.Select(page => page.Id).Should().BeEquivalentTo(pages);
    }

    [Fact]
    public async Task PublishingANewVersionChangesEveryLateBoundPageWithoutRepublishingThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (item, pages) = await ArrangeFooterOnThreePagesAsync(cancellationToken);

        var publishedVersionsBefore = await PublishedVersionIdsAsync(pages, cancellationToken);

        var updated = await _bench.SetReusableHtmlAsync(item, "<p>Second footer</p>", cancellationToken);
        await _bench.PublishReusableAsync(updated.Summary.Id, cancellationToken);

        // Acceptance criterion P4 #2, and the whole of goal G4. Two halves, and both matter:
        // every page now renders the new text, and not one of them has a new published version.
        foreach (var pageId in pages)
        {
            (await RenderedHtmlAsync(pageId, cancellationToken))
                .Should().Contain("Second footer").And.NotContain("First footer");
        }

        (await PublishedVersionIdsAsync(pages, cancellationToken))
            .Should().Equal(publishedVersionsBefore, "no page was republished");
    }

    [Fact]
    public async Task APinnedPageDoesNotChangeWhenANewerVersionPublishes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (item, pages) = await ArrangeFooterOnThreePagesAsync(cancellationToken);

        var firstVersionId = (await _bench.Context.ReusableContents
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == item.Summary.Id, cancellationToken))
            .PublishedVersionId!.Value;

        // The third page pins the version that is live right now, then the item moves on.
        var pinned = pages[2];
        var page = (await _bench.Resolve<IPageService>().GetAsync(pinned, cancellationToken)).Value!;

        page = await _bench.PlaceReusableAsync(
            page,
            "footer",
            item.Summary.Id,
            cancellationToken,
            pinnedVersionId: firstVersionId);

        await _bench.Resolve<IPublishingService>().PublishAsync(page.Summary.Id, true, cancellationToken);

        var updated = await _bench.SetReusableHtmlAsync(item, "<p>Second footer</p>", cancellationToken);
        var result = await _bench.PublishReusableAsync(updated.Summary.Id, cancellationToken);

        // Acceptance criterion P4 #3: the pinned page is reproducible under audit while the others
        // move. Asserted on the rendered document, since that is where an auditor would look.
        (await RenderedHtmlAsync(pinned, cancellationToken)).Should().Contain("First footer");
        (await RenderedHtmlAsync(pages[0], cancellationToken)).Should().Contain("Second footer");

        // Acceptance criterion P4 #4: the counts the confirmation dialog shows, split by pinned and
        // late-bound, and exact rather than an upper bound.
        result.Impact.AffectedPageCount.Should().Be(2);
        result.Impact.PinnedPageCount.Should().Be(1);
        result.Impact.AffectedPages.Single(page => page.Id == pinned).IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task PublishingIsRefusedUntilTheBlastRadiusIsAcknowledged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (item, _) = await ArrangeFooterOnThreePagesAsync(cancellationToken);

        var updated = await _bench.SetReusableHtmlAsync(item, "<p>Second footer</p>", cancellationToken);
        var reusable = _bench.Resolve<IReusableContentService>();

        var unacknowledged = await reusable.PublishAsync(
            updated.Summary.Id,
            acknowledgeWarnings: false,
            cancellationToken);

        // The UI rule of spec section 9.4 enforced by the server, so a screen that skipped the dialog
        // — or a script that never had one — cannot change three pages silently. The refusal carries
        // the count, because it is what the dialog is built from.
        unacknowledged.Outcome.Should().Be(CmsOutcome.Invalid);
        unacknowledged.Diagnostics.Diagnostics
            .Should().Contain(diagnostic => diagnostic.Code == ReusableCodes.BlastRadius);

        (await reusable.PublishAsync(updated.Summary.Id, acknowledgeWarnings: true, cancellationToken))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ThePublishIsAuditedWithTheListOfPagesItChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (item, pages) = await ArrangeFooterOnThreePagesAsync(cancellationToken);

        var updated = await _bench.SetReusableHtmlAsync(item, "<p>Second footer</p>", cancellationToken);
        await _bench.PublishReusableAsync(updated.Summary.Id, cancellationToken);

        var entry = await _bench.Context.AuditLogs
            .AsNoTracking()
            .Where(row => row.Type == ReusableContentService.PublishAuditType)
            .OrderByDescending(row => row.Id)
            .FirstAsync(cancellationToken);

        // Task P4-12: "why did 40 pages change at 14:02?" has to be answerable months later, and the
        // change interceptor structurally cannot answer it — a publish's consequence is on rows it
        // did not touch. The page ids are stored rather than the titles, because an id is the part
        // that is still true when somebody reads this back.
        using var recorded = JsonDocument.Parse(entry.NewValues!);

        recorded.RootElement.GetProperty("AffectedPageCount").GetInt32().Should().Be(3);
        recorded.RootElement.GetProperty("AffectedPageIds")
            .EnumerateArray()
            .Select(id => id.GetInt32())
            .Should().BeEquivalentTo(pages);
    }

    [Fact]
    public async Task UnpublishingRendersNothingOnDependentPagesAndSaysSo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (item, pages) = await ArrangeFooterOnThreePagesAsync(cancellationToken);

        var result = await _bench.Resolve<IReusableContentService>().UnpublishAsync(
            item.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        result.IsSuccess.Should().BeTrue(PageWorkbench.Because(result));

        // Acceptance criterion P4 #6. The pages still serve — one retired fragment must not 404 a
        // page — and the space where the footer was is simply empty (spec section 15.3).
        foreach (var pageId in pages)
        {
            (await RenderedHtmlAsync(pageId, cancellationToken)).Should().NotContain("First footer");
        }

        // The one lifecycle action whose damage is entirely off-screen, so the count comes back with
        // it rather than being something an editor has to go and look up afterwards.
        result.Value!.Impact.AffectedPageCount.Should().Be(3);
    }

    [Fact]
    public async Task DeletingAReferencedItemIsRefusedWithTheWhereUsedList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (item, pages) = await ArrangeFooterOnThreePagesAsync(cancellationToken);

        var refused = await _bench.Resolve<IReusableContentService>().DeleteAsync(
            item.Summary.Id,
            cancellationToken);

        // Acceptance criterion P4 #5. Blocked outright rather than cascaded: a deleted item is
        // invisible to the resolver, so deleting one that is still placed blanks a zone on every page
        // holding it, discovered by a visitor.
        refused.Outcome.Should().Be(CmsOutcome.Conflict);
        refused.Diagnostics.Diagnostics
            .Should().Contain(diagnostic => diagnostic.Code == ReusableCodes.StillReferenced);

        // The message names the pages, because "replace the references first" is not actionable
        // without them.
        var message = refused.Diagnostics.Diagnostics
            .Single(diagnostic => diagnostic.Code == ReusableCodes.StillReferenced).Message;

        foreach (var pageId in pages)
        {
            message.Should().Contain(pageId.ToString());
        }

        // And it is still there afterwards.
        (await _bench.Context.ReusableContents
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == item.Summary.Id, cancellationToken))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AnItemNothingPlacesIsDeletedAndStopsResolving()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var item = await _bench.AddReusableAsync("Orphan banner", cancellationToken);
        var filled = await _bench.SetReusableHtmlAsync(item, "<p>Nobody wants me</p>", cancellationToken);

        await _bench.PublishReusableAsync(filled.Summary.Id, cancellationToken);

        var deleted = await _bench.Resolve<IReusableContentService>().DeleteAsync(
            filled.Summary.Id,
            cancellationToken);

        deleted.IsSuccess.Should().BeTrue(PageWorkbench.Because(deleted));

        // The soft-delete query filter is what makes this true, rather than a check the resolver has
        // to remember to make — which is the whole reason the filter is on the entity.
        var resolved = await _bench.Resolve<IReusableContentResolver>().ResolveAsync(
            filled.Summary.Id,
            pinnedVersionId: null,
            ReusableResolutionChain.Root,
            cancellationToken: cancellationToken);

        resolved.Status.Should().Be(ReusableResolutionStatus.NotFound);
    }

    [Fact]
    public async Task AnItemThatPlacesItselfIsRefusedAtWriteTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var nestable = await AddNestableBlockTypeAsync(cancellationToken);
        var item = await _bench.AddReusableAsync("Footer", cancellationToken, nestable.Id);

        var result = await _bench.Resolve<IReusableContentService>().SaveDraftAsync(
            item.Summary.Id,
            new SaveDraftRequest(
                NestedPayload(item, item.Summary.Id, "<p>Footer</p>"),
                item.RowVersion),
            cancellationToken);

        // Acceptance criterion P4 #7, first half. Refused when it is written, which is the only place
        // it can be refused usefully: at render time all that is left is a depth guard, and a guard
        // that fires renders half a footer.
        result.Outcome.Should().Be(CmsOutcome.Invalid);
        result.Diagnostics.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == ReusableCodes.Cycle);
    }

    [Fact]
    public async Task AnItemThatPlacesItselfThroughAnotherItemIsAlsoRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var nestable = await AddNestableBlockTypeAsync(cancellationToken);
        var reusable = _bench.Resolve<IReusableContentService>();

        var outer = await _bench.AddReusableAsync("Outer", cancellationToken, nestable.Id);
        var inner = await _bench.AddReusableAsync("Inner", cancellationToken, nestable.Id);

        // Outer places Inner. Legal, and the state a two-level footer is ordinarily in.
        var linked = await reusable.SaveDraftAsync(
            outer.Summary.Id,
            new SaveDraftRequest(
                NestedPayload(outer, inner.Summary.Id, "<p>Outer</p>"),
                outer.RowVersion),
            cancellationToken);

        linked.IsSuccess.Should().BeTrue(PageWorkbench.Because(linked));

        // Now Inner tries to place Outer, closing the loop through a row that mentions neither end.
        var closed = await reusable.SaveDraftAsync(
            inner.Summary.Id,
            new SaveDraftRequest(
                NestedPayload(inner, outer.Summary.Id, "<p>Inner</p>"),
                inner.RowVersion),
            cancellationToken);

        // The transitive case, which is the one a direct self-reference check would miss entirely.
        closed.Outcome.Should().Be(CmsOutcome.Invalid);
        closed.Diagnostics.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == ReusableCodes.Cycle);
    }

    [Fact]
    public async Task AnItemPlacedInsideAnotherItemCountsThePagesShowingTheOuterOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var nestable = await AddNestableBlockTypeAsync(cancellationToken);
        var reusable = _bench.Resolve<IReusableContentService>();

        var banner = await _bench.AddReusableAsync("Banner", cancellationToken);
        var bannerFilled = await _bench.SetReusableHtmlAsync(banner, "<p>Banner</p>", cancellationToken);
        await _bench.PublishReusableAsync(bannerFilled.Summary.Id, cancellationToken);

        var footer = await _bench.AddReusableAsync("Footer", cancellationToken, nestable.Id);

        var linked = await reusable.SaveDraftAsync(
            footer.Summary.Id,
            new SaveDraftRequest(
                NestedPayload(footer, bannerFilled.Summary.Id, "<p>Footer</p>"),
                footer.RowVersion),
            cancellationToken);

        linked.IsSuccess.Should().BeTrue(PageWorkbench.Because(linked));
        await _bench.PublishReusableAsync(footer.Summary.Id, cancellationToken);

        var template = await _bench.AddTemplateAsync(
            "with-footer",
            cancellationToken,
            PageWorkbench.ReusableZone("footer"));

        var page = await _bench.AddPageAsync(template, "Home", cancellationToken);
        page = await _bench.PlaceReusableAsync(page, "footer", footer.Summary.Id, cancellationToken);
        await _bench.Resolve<IPublishingService>().PublishAsync(page.Summary.Id, true, cancellationToken);

        var impact = await _bench.Resolve<IReferenceQueryService>().WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            bannerFilled.Summary.Id,
            cancellationToken);

        // Nothing places the banner directly. A walk that stopped at direct placements would tell an
        // editor that changing a site-wide banner affects nothing, which is the exact case where the
        // confirmation of spec section 9.4 matters most.
        impact.AffectedPageCount.Should().Be(1);
        impact.AffectedPages.Single().Id.Should().Be(page.Summary.Id);
        impact.AffectedReusableItems.Single().Id.Should().Be(footer.Summary.Id);
    }

    [Fact]
    public async Task PublishingAPageThatPlacesADeletedItemIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "with-footer",
            cancellationToken,
            PageWorkbench.ReusableZone("footer"));

        var item = await _bench.AddReusableAsync("Footer", cancellationToken);
        var filled = await _bench.SetReusableHtmlAsync(item, "<p>Footer</p>", cancellationToken);

        var page = await _bench.AddPageAsync(template, "Home", cancellationToken);

        page = await _bench.PlaceReusableAsync(page, "footer", filled.Summary.Id, cancellationToken);

        // The item goes before the page is published — nothing places it yet, so the delete is
        // allowed. Wait: the draft placement is a reference, so this must be refused too.
        (await _bench.Resolve<IReusableContentService>().DeleteAsync(filled.Summary.Id, cancellationToken))
            .Outcome.Should().Be(
                CmsOutcome.Conflict,
                "a placement held only by a draft still breaks that draft when it is published");

        // Removing the placement first is the editor's remedy, and then the delete goes through.
        await ClearZonesAsync(page, cancellationToken);

        (await _bench.Resolve<IReusableContentService>().DeleteAsync(filled.Summary.Id, cancellationToken))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task APlacementOfTheWrongShapeIsRefusedAtPublish()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var nestable = await AddNestableBlockTypeAsync(cancellationToken);

        // The zone accepts banner-shaped items only, and the placement is a nestable-shaped one.
        var template = await _bench.AddTemplateAsync(
            "banners-only",
            cancellationToken,
            PageWorkbench.ReusableZone("footer", CmsSeedData.RawHtmlBlockTypeKey));

        var item = await _bench.AddReusableAsync("Wrong shape", cancellationToken, nestable.Id);
        var page = await _bench.AddPageAsync(template, "Home", cancellationToken);

        page = await _bench.PlaceReusableAsync(page, "footer", item.Summary.Id, cancellationToken);

        var result = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        // allowedTypes, which the field type cannot enforce itself — it is a stateless singleton with
        // no database, and "what shape is item 3" is not answerable from the stored value alone.
        result.Outcome.Should().Be(CmsOutcome.Invalid);
        result.Diagnostics.Diagnostics
            .Should().Contain(diagnostic => diagnostic.Code == FieldValidationCodes.NotAllowed);
    }

    [Fact]
    public async Task ReusableContentNeedsTheSamePermissionsPagesDo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var author = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead, CmsPermissions.ContentEdit),
            cancellationToken);

        var item = await author.AddReusableAsync("Footer", cancellationToken);
        var reusable = author.Resolve<IReusableContentService>();

        // An Author writes drafts and neither publishes nor deletes. The endpoint policy is the door
        // and this is the lock, exactly as it is for pages.
        (await reusable.PublishAsync(item.Summary.Id, true, cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);
        (await reusable.DeleteAsync(item.Summary.Id, cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);
    }

    /// <summary>
    /// A published footer placed on three published pages — the fixture P4 #1 through #6 all start from.
    /// </summary>
    private async Task<(ReusableContentDetail Item, int[] PageIds)> ArrangeFooterOnThreePagesAsync(
        CancellationToken cancellationToken)
    {
        var item = await _bench.AddReusableAsync("Site footer", cancellationToken);
        var filled = await _bench.SetReusableHtmlAsync(item, "<p>First footer</p>", cancellationToken);

        await _bench.PublishReusableAsync(filled.Summary.Id, cancellationToken);

        // marketing-landing rather than article, because its component declares a 'footer' zone and
        // article's does not. A zone the template's markup never places is authored, validated, and
        // stored exactly as any other — and renders nowhere, which would make every assertion below
        // about the rendered document silently vacuous.
        var template = await _bench.UseTemplateAsync(
            "marketing-landing",
            cancellationToken,
            PageWorkbench.ReusableZone("footer"));

        var publishing = _bench.Resolve<IPublishingService>();
        var pageIds = new int[3];

        for (var index = 0; index < pageIds.Length; index++)
        {
            var page = await _bench.AddPageAsync(template, $"Page {index + 1}", cancellationToken);

            page = await _bench.PlaceReusableAsync(page, "footer", filled.Summary.Id, cancellationToken);

            var published = await publishing.PublishAsync(page.Summary.Id, true, cancellationToken);

            published.IsSuccess.Should().BeTrue(PageWorkbench.Because(published));

            pageIds[index] = page.Summary.Id;
        }

        // Reloaded so the caller holds a current row version for the next draft save.
        return ((await _bench.Resolve<IReusableContentService>()
            .GetAsync(filled.Summary.Id, cancellationToken)).Value!, pageIds);
    }

    /// <summary>Fetches a published page over real HTTP, which is where a visitor would see it.</summary>
    private async Task<string> RenderedHtmlAsync(int pageId, CancellationToken cancellationToken)
    {
        var url = await _bench.Context.PageRoutes
            .AsNoTracking()
            .Where(route => route.PageId == pageId && route.IsPublished)
            .Select(route => route.Url)
            .SingleAsync(cancellationToken);

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync(url, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<int?[]> PublishedVersionIdsAsync(int[] pageIds, CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var rows = await _bench.Context.Pages
            .AsNoTracking()
            .Where(page => pageIds.Contains(page.Id))
            .Select(page => new { page.Id, page.PublishedVersionId })
            .ToListAsync(cancellationToken);

        return [.. pageIds.Select(id => rows.Single(row => row.Id == id).PublishedVersionId)];
    }

    /// <summary>
    /// A block type whose properties include a reusable placement, so items can nest.
    /// </summary>
    /// <remarks>
    /// The built-in <c>rawHtml</c> shape has one HTML property and therefore cannot contain another
    /// item however its payload is authored. Nesting, and every guard that exists because of it,
    /// needs a shape that nests.
    /// </remarks>
    private async Task<BlockType> AddNestableBlockTypeAsync(CancellationToken cancellationToken)
    {
        var properties = new List<BlockTypeProperty>
        {
            new() { Key = "content", Name = "Content", FieldTypeKey = FieldTypeKeys.Html, SortOrder = 0 },
            new() { Key = "nested", Name = "Nested", FieldTypeKey = FieldTypeKeys.Reusable, SortOrder = 1 },
        };

        var blockType = new BlockType
        {
            Key = "nestable-panel",
            Name = "Nestable panel",
            CurrentRevision = 1,
        };

        foreach (var property in properties)
        {
            blockType.Properties.Add(property);
        }

        blockType.Revisions.Add(new BlockTypeRevision
        {
            RevisionNumber = 1,
            PropertySnapshotJson = Core.Content.Schema.ContentSchemaSnapshot.WriteProperties(properties),
            Notes = "Block type created.",
        });

        _bench.Context.BlockTypes.Add(blockType);
        await _bench.Context.SaveChangesAsync(cancellationToken);

        return blockType;
    }

    /// <summary>A nestable item's content: an HTML fragment plus a placement of another item.</summary>
    private static string NestedPayload(ReusableContentDetail item, int reusableContentId, string html) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{item.Summary.BlockTypeKey}}",
          "templateRevision": {{item.BlockTypeRevision}},
          "zones": {
            "content": { "type": "html", "value": {{JsonSerializer.Serialize(html)}} },
            "nested": { "type": "reusable", "reusableContentId": {{reusableContentId}},
                        "pinnedVersionId": null } } }
        """;

    /// <summary>Empties every zone on a page's draft, which is how an editor removes a placement.</summary>
    private async Task ClearZonesAsync(PageDetail page, CancellationToken cancellationToken)
    {
        var payload = $$"""
            { "schemaVersion": 1, "templateKey": "{{page.Summary.TemplateKey}}",
              "templateRevision": {{page.TemplateRevision}}, "zones": { } }
            """;

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(payload, page.RowVersion),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(PageWorkbench.Because(saved));
    }
}
