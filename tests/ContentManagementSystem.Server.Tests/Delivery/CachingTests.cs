using System.Net;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// Output caching and the invalidation that keeps it honest (tasks P8-21, P8-22, P8-24).
/// </summary>
/// <remarks>
/// The outbox is dispatched by calling <see cref="OutboxRunner"/> directly rather than by waiting
/// for the hosted service's timer. That is what makes "the page is still the cached one until the
/// outbox runs, and the new one immediately afterwards" assertable at all — and the hosted service
/// itself is a timer around this same call, with nothing of its own to test.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class CachingTests(SqlServerFixture fixture)
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
    public async Task APageIsServedFromTheCacheUntilPublishingEvictsIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Pricing", "First words", cancellationToken);

        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        (await client.GetStringAsync("/pricing", cancellationToken)).Should().Contain("First words");

        await SaveDraftAsync(page.Summary.Id, "Second words", cancellationToken);
        await PublishAsync(page.Summary.Id, cancellationToken);

        // The publish committed and its eviction is sitting in the outbox, undispatched. The cached
        // response is what proves there was a cache to evict — without one this assertion would see
        // the new text already.
        (await client.GetStringAsync("/pricing", cancellationToken)).Should().Contain("First words");

        await DispatchAsync(cancellationToken);

        // Acceptance criterion P8 #4.
        (await client.GetStringAsync("/pricing", cancellationToken)).Should().Contain("Second words");
    }

    [Test]
    public async Task PublishingOnePageLeavesEveryOtherPagesCacheEntryAlone()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var subject = await PublishedPageAsync("Pricing", "Subject content", cancellationToken);
        var bystander = await PublishedPageAsync("Support", "Bystander content", cancellationToken);

        // Drained before anything is cached. Each of those publishes enqueued its own eviction, and
        // dispatching them afterwards would evict entries created after the publish they describe —
        // which is what the poller's five-second cadence makes a non-issue in a running site and a
        // false failure in a test that batches its fixtures.
        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        await client.GetStringAsync("/pricing", cancellationToken);
        await client.GetStringAsync("/support", cancellationToken);

        // The bystander's stored content is changed behind the cache's back, with no publish and so
        // no eviction. Its response can only stay the same if its entry survived the subject's
        // publish — which is the half of "exactly its own entry" that a positive test cannot show.
        await RewriteStoredContentAsync(bystander.Summary.Id, "Bystander rewritten", cancellationToken);

        await SaveDraftAsync(subject.Summary.Id, "Subject republished", cancellationToken);
        await PublishAsync(subject.Summary.Id, cancellationToken);
        await DispatchAsync(cancellationToken);

        (await client.GetStringAsync("/pricing", cancellationToken)).Should().Contain("Subject republished");
        (await client.GetStringAsync("/support", cancellationToken))
            .Should().Contain("Bystander content").And.NotContain("Bystander rewritten");
    }

    [Test]
    public async Task PublishingAReusableItemEvictsThePagesThatRenderItAndNothingElse()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var host = await PublishedPageAsync("Offers", "Host page", cancellationToken);
        var bystander = await PublishedPageAsync("Support", "Bystander content", cancellationToken);

        await DispatchAsync(cancellationToken);

        // The dependency is declared by the render rather than by a table: this stands in for it by
        // tagging the host page's entry the way a reusable placement would.
        using var client = _bench.CreateClient();

        await client.GetStringAsync("/offers", cancellationToken);
        await client.GetStringAsync("/support", cancellationToken);

        await RewriteStoredContentAsync(host.Summary.Id, "Host rewritten", cancellationToken);
        await RewriteStoredContentAsync(bystander.Summary.Id, "Bystander rewritten", cancellationToken);

        await using (var scope = _bench.NewScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<ICacheInvalidationQueue>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            queue.Enqueue([Core.Caching.CacheTags.Page(host.Summary.Id)]);
            await context.SaveChangesAsync(cancellationToken);
        }

        await DispatchAsync(cancellationToken);

        // Acceptance criterion P8 #5, in the shape the tag scheme gives it: what was evicted is
        // exactly what carried the tag.
        (await client.GetStringAsync("/offers", cancellationToken)).Should().Contain("Host rewritten");
        (await client.GetStringAsync("/support", cancellationToken)).Should().Contain("Bystander content");
    }

    [Test]
    public async Task AnInvalidationEnqueuedInATransactionThatFailsIsNeverDispatched()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _bench.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<ICacheInvalidationQueue>();

        // Through the execution strategy, like every explicit transaction in this application: the
        // retrying strategy Aspire configures refuses a user-initiated transaction outside one.
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            queue.Enqueue([Core.Caching.CacheTags.Page(4242)]);
            await context.SaveChangesAsync(cancellationToken);

            await transaction.RollbackAsync(cancellationToken);
        });

        context.ChangeTracker.Clear();

        // Half of acceptance criterion P8 #8. The row is written by the same SaveChanges as the work
        // that caused it, so a rollback takes it with it and there is nothing left to dispatch.
        (await context.OutboxMessages.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Test]
    public async Task AnInvalidationCommittedBeforeAProcessDiesIsDispatchedByTheNextOneToStart()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Pricing", "First words", cancellationToken);

        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        await client.GetStringAsync("/pricing", cancellationToken);

        await SaveDraftAsync(page.Summary.Id, "Second words", cancellationToken);
        await PublishAsync(page.Summary.Id, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.ProcessedOn == null, cancellationToken))
            .Should().BePositive();

        // A process that started after the publish committed: a fresh watermark, no memory of what
        // came before. The other half of acceptance criterion P8 #8 — the commit is what makes the
        // eviction certain, not the call that would have followed it.
        await using var scope = _bench.NewScope();

        var restarted = new OutboxRunner(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<ICacheInvalidator>(),
            new OutboxState(),
            scope.ServiceProvider.GetRequiredService<IOptions<OutboxOptions>>(),
            TimeProvider.System,
            NullLogger<OutboxRunner>.Instance);

        (await restarted.RunOnceAsync(cancellationToken)).Should().BePositive();

        (await client.GetStringAsync("/pricing", cancellationToken)).Should().Contain("Second words");
    }

    [Test]
    public async Task AnEditorsRequestIsNeitherServedFromNorStoredInTheAnonymousCache()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Pricing", "Cached words", cancellationToken);

        await DispatchAsync(cancellationToken);

        using var anonymous = _bench.CreateClient();
        using var editor = _bench.CreateClient(followRedirects: true, CmsRoles.Editor);

        await anonymous.GetStringAsync("/pricing", cancellationToken);

        // Changed behind the cache's back, so the two audiences can be told apart by what they see.
        await RewriteStoredContentAsync(page.Summary.Id, "Uncached words", cancellationToken);

        // Acceptance criterion P8 #6, first half: the editor is never handed the anonymous entry.
        (await editor.GetStringAsync("/pricing", cancellationToken))
            .Should().Contain("Uncached words");

        // Second half: and their response did not become the anonymous one either.
        (await anonymous.GetStringAsync("/pricing", cancellationToken))
            .Should().Contain("Cached words");
    }

    [Test]
    public async Task TheOutboxPrunesWhatItHasDispatched()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _bench.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = CacheInvalidationMessage.MessageType,
            PayloadJson = new CacheInvalidationMessage([Core.Caching.CacheTags.Page(7)]).ToJson(),
            CreatedOn = DateTimeOffset.UtcNow.AddDays(-3),
            ProcessedOn = DateTimeOffset.UtcNow.AddDays(-3),
        });

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);

        // Kept for a day so "why did this page not update" has an answer, and pruned after that so
        // the table does not grow with the site's publish history.
        (await context.OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.ProcessedOn != null, cancellationToken))
            .Should().Be(0);
    }

    [Test]
    public async Task AMessageThatCannotBeAppliedIsCountedAndLeftPendingRatherThanBlockingTheQueue()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _bench.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = CacheInvalidationMessage.MessageType,
            PayloadJson = "{ this is not json",
            CreatedOn = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);

        // Unreadable rather than unevictable: it is skipped and marked dispatched, because a row
        // nothing can ever read is not going to become readable on the next pass and must not stop
        // every invalidation enqueued behind it.
        (await context.OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.ProcessedOn == null, cancellationToken))
            .Should().Be(0);
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _bench.NewScope();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);
    }

    /// <summary>
    /// Rewrites a published version's stored content without publishing anything.
    /// </summary>
    /// <remarks>
    /// Deliberately behind the cache's back: it changes what the database says without enqueuing an
    /// invalidation, which is how a test can tell a served-from-cache response from a re-rendered
    /// one.
    /// </remarks>
    private async Task RewriteStoredContentAsync(int pageId, string text, CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var versionId = await _bench.Context.Pages
            .AsNoTracking()
            .Where(page => page.Id == pageId)
            .Select(page => page.PublishedVersionId!.Value)
            .SingleAsync(cancellationToken);

        await _bench.Context.PageVersions
            .Where(version => version.Id == versionId)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(version => version.ContentJson, Payload(text)),
                cancellationToken);

        // The content cache alone, through HybridCache directly rather than through the invalidator:
        // the point of the rewrite is to change what a *re-render* would produce while leaving the
        // stored response untouched, so that a request answered from the output cache and one that
        // rendered can be told apart.
        await _bench.Resolve<HybridCache>().RemoveByTagAsync(
            Core.Caching.CacheTags.Page(pageId),
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();
    }

    private async Task<Template> TemplateAsync(CancellationToken cancellationToken) =>
        _template ??= await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

    private async Task<PageDetail> PublishedPageAsync(
        string title,
        string text,
        CancellationToken cancellationToken)
    {
        var template = await TemplateAsync(cancellationToken);
        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        await SaveDraftAsync(page.Summary.Id, text, cancellationToken);
        await PublishAsync(page.Summary.Id, cancellationToken);

        return page;
    }

    private async Task PublishAsync(int pageId, CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(pageId, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
        _bench.Context.ChangeTracker.Clear();
    }

    private async Task SaveDraftAsync(int pageId, string text, CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            pageId,
            new SaveDraftRequest(Payload(text), null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();
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
