using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Search;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Search;

/// <summary>
/// The backoffice search index: what reaches it, when, and what the filters do (tasks P8-18, P8-19).
/// </summary>
/// <remarks>
/// The outbox is dispatched by calling <see cref="OutboxRunner"/> directly, as the caching suite
/// does, because "not findable yet, findable after the poller runs" is the actual behaviour and a
/// test that waited five seconds for the hosted service would be asserting the timer instead.
/// <para>
/// These run on whichever engine the fixture started: SQL Server answers with the full-text index,
/// Azure SQL Edge with the fallback scan. Both are supported deployments, both return the same rows,
/// and asserting the rows rather than the plan is what lets one suite cover them (spec section 17.1).
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class SearchTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "article";

    private PageWorkbench _bench = null!;
    private Template? _template;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task ADraftIsFindableByItsBodyTextOnceTheOutboxHasRun()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Gearboxes", "The helical gearbox is rated to 40 kilowatts", cancellationToken);

        // Before the poller runs there is nothing to find, which is the cost of indexing
        // asynchronously and is stated here rather than left as folklore.
        (await SearchAsync(new SearchQuery("gearbox"), cancellationToken)).Hits.Should().BeEmpty();

        await DispatchAsync(cancellationToken);

        var results = await SearchAsync(new SearchQuery("gearbox"), cancellationToken);

        // Working content, not published content: this page has never been published, and an editor
        // looking for what they wrote this morning would not find it in an index of what is live.
        results.Hits.Should().ContainSingle();
        results.Hits[0].Id.Should().Be(page.Summary.Id);
        results.Hits[0].Kind.Should().Be(SearchResultKind.Page);
        results.Hits[0].IsPublished.Should().BeFalse();
    }

    [Test]
    public async Task PublishingAPageMarksItsDocumentPublishedAndRecyclingRemovesIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Pricing", "Everything about our prices", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();
        await DispatchAsync(cancellationToken);

        var published = await SearchAsync(new SearchQuery("prices"), cancellationToken);

        published.Hits.Should().ContainSingle();
        published.Hits[0].IsPublished.Should().BeTrue();
        published.Hits[0].Url.Should().Be("/pricing");

        (await _bench.Resolve<IRecycleBinService>()
            .DeleteAsync(page.Summary.Id, cancellationToken)).IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();
        await DispatchAsync(cancellationToken);

        // A recycled page is not a hit with a flag on it: the indexer reads the source, finds the
        // query filter hiding it, and removes the document.
        (await SearchAsync(new SearchQuery("prices"), cancellationToken)).Hits.Should().BeEmpty();
    }

    [Test]
    public async Task TheFiltersNarrowToPagesAndTheyCompose()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var reviewed = await DraftPageAsync("Warranty", "Cover for the first year", cancellationToken);
        await DraftPageAsync("Careers", "Cover letters welcome", cancellationToken);

        var patched = await _bench.Resolve<IPageService>().PatchMetadataAsync(
            reviewed.Summary.Id,
            new PatchPageMetadataRequest
            {
                ReviewByDate = new Patch<DateOnly?>(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30)),
                Tags = new Patch<IReadOnlyList<string>>(["Product docs"]),
            },
            null,
            cancellationToken);

        patched.IsSuccess.Should().BeTrue(Because(patched));

        _bench.Context.ChangeTracker.Clear();
        await DispatchAsync(cancellationToken);

        // "cover" matches both pages, so anything that comes back to one of them is the filter
        // doing the work rather than the text.
        (await SearchAsync(new SearchQuery("cover"), cancellationToken)).Hits.Should().HaveCount(2);

        var overdue = await SearchAsync(new SearchQuery("cover", PastReviewDate: true), cancellationToken);

        overdue.Hits.Should().ContainSingle();
        overdue.Hits[0].Id.Should().Be(reviewed.Summary.Id);

        var tagged = await SearchAsync(new SearchQuery("cover", Tag: "product-docs"), cancellationToken);

        // By slug, which is what a tag chip carries, though the filter takes either.
        tagged.Hits.Should().ContainSingle();
        tagged.Hits[0].Id.Should().Be(reviewed.Summary.Id);

        var unpublished = await SearchAsync(
            new SearchQuery("cover", HasUnpublishedChanges: true),
            cancellationToken);

        // Neither page has ever been published, which is a state the "needs publishing" list has to
        // include: an editor asking what still needs publishing means these too.
        unpublished.Hits.Should().HaveCount(2);
    }

    [Test]
    public async Task ANonsenseStatusIsRefusedRatherThanIgnored()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _bench.NewScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISearchService>()
            .SearchAsync(new SearchQuery(Status: "nearly-done"), cancellationToken);

        // A filter that quietly matched everything would read as "every page is a draft", which is a
        // far more expensive thing to debug than a refusal.
        result.IsSuccess.Should().BeFalse();
        result.Diagnostics.Diagnostics.Should().Contain(
            diagnostic => diagnostic.Code == SearchCodes.UnknownStatus);
    }

    [Test]
    public async Task TheReconcileRebuildsADocumentThatWasLostAndRemovesOneThatIsOrphaned()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Shipping", "Delivered within five days", cancellationToken);

        await DispatchAsync(cancellationToken);

        (await SearchAsync(new SearchQuery("delivered"), cancellationToken)).Hits.Should().ContainSingle();

        // Standing in for every way an asynchronous index can go wrong — a message dropped, an
        // instance that died mid-batch, a write path added later that forgot to enqueue. The
        // reconcile does not know which of those happened, and does not need to.
        await _bench.Context.SearchDocuments
            .Where(document => document.EntityType == SearchEntityKind.Page)
            .ExecuteDeleteAsync(cancellationToken);

        _bench.Context.SearchDocuments.Add(new SearchDocument
        {
            EntityType = SearchEntityKind.Page,
            EntityId = 987654,
            Title = "A page that does not exist",
            UpdatedOn = DateTimeOffset.UtcNow,
        });

        await _bench.Context.SaveChangesAsync(cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        await using (var scope = _bench.NewScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<ISearchIndexer>()
                .ReconcileAsync(cancellationToken);

            report.FoundNothingWrong.Should().BeFalse();
            report.Removed.Should().BeGreaterThan(0);
        }

        (await SearchAsync(new SearchQuery("delivered"), cancellationToken)).Hits
            .Should().ContainSingle().Which.Id.Should().Be(page.Summary.Id);

        (await _bench.Context.SearchDocuments
            .AsNoTracking()
            .AnyAsync(document => document.EntityId == 987654, cancellationToken))
            .Should().BeFalse();
    }

    [Test]
    public async Task AnIndexMessageIsAppliedOnceEvenWhenTwoInstancesPollTogether()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await DraftPageAsync("Returns", "Send it back within thirty days", cancellationToken);

        // Two runners with independent watermarks, which is what a second instance is. The cache
        // handler applies on both by design; the index handler claims the row, so exactly one of
        // these does the work — and the assertion is that the index is right either way rather than
        // that a particular runner won.
        await using var first = _bench.NewScope();
        await using var second = _bench.NewScope();

        await first.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);
        await second.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.SearchDocuments
            .AsNoTracking()
            .CountAsync(document => document.EntityType == SearchEntityKind.Page, cancellationToken))
            .Should().Be(1);
    }

    private async Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        await using var scope = _bench.NewScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISearchService>()
            .SearchAsync(query, cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        return result.Value!;
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _bench.NewScope();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);
    }

    private async Task<Template> TemplateAsync(CancellationToken cancellationToken) =>
        _template ??= await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

    private async Task<PageDetail> DraftPageAsync(
        string title,
        string text,
        CancellationToken cancellationToken)
    {
        var template = await TemplateAsync(cancellationToken);
        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(text), null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();

        return page;
    }

    private static string Payload(string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{TemplateKey}}", "templateRevision": 1,
          "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
        """;

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
