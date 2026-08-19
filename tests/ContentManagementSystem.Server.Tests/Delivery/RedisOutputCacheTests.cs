using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.ServiceDefaults;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Testcontainers.Redis;

using TUnit.Core.Interfaces;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>Starts a disposable Redis container for the scale-out tests.</summary>
/// <remarks>
/// Shared for the session, like the SQL Server fixture and for the same reason: container start-up
/// costs seconds and these tests need one instance between them, not one each.
/// </remarks>
public sealed class RedisFixture : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>The image, pinned rather than floating on <c>latest</c>, like the SQL Server one.</summary>
    private const string RedisImage = "redis:7.4-alpine";


    private readonly RedisContainer _container = new RedisBuilder(RedisImage).Build();

    /// <summary>Connection string the output cache is pointed at.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// Two instances behind one Redis, which is the only supported way to run more than one
/// (task P8-23, spec section 16.3).
/// </summary>
/// <remarks>
/// Both instances run against the same database and the same Redis, exactly as a scaled-out
/// deployment does — including the part that is easy to leave out of a test: <em>both</em> poll the
/// outbox. The output cache is shared, so one eviction serves every node; the published-content
/// cache is in-process, so each node has to apply the message itself. A test that dispatched on one
/// node only would pass against a design that leaves the other serving stale content.
/// </remarks>
[ClassDataSource<SqlServerFixture, RedisFixture>(Shared = [SharedType.PerTestSession, SharedType.PerTestSession])]
[NotInParallel(SqlServerConstraint.Key)]
public class RedisOutputCacheTests(SqlServerFixture sql, RedisFixture redis)
{
    private const string TemplateKey = "article";

    [Test]
    public async Task AnEvictionOnOneInstanceReachesTheOthersOutputCache()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var database = $"cms_redis_{Guid.NewGuid():N}";

        await using (await sql.CreateDatabaseAsync(database, cancellationToken)) { }

        await using var instanceA = Instance(database);
        await using var instanceB = Instance(database);

        var storeA = instanceA.Services.GetRequiredService<IOutputCacheStore>();
        var storeB = instanceB.Services.GetRequiredService<IOutputCacheStore>();

        // Both hosts resolved the Redis store rather than the in-memory one. Without this the two
        // assertions below would still pass on a single host and prove nothing at all.
        storeA.GetType().Name.Should().StartWith("RedisOutputCacheStore");
        storeB.GetType().Name.Should().StartWith("RedisOutputCacheStore");

        var key = $"probe:{Guid.NewGuid():N}";
        var tag = CacheTags.Page(4242);

        await storeA.SetAsync(key, [1, 2, 3], [tag], TimeSpan.FromMinutes(5), cancellationToken);

        // The store itself is shared, which is the property the whole scale-out story rests on.
        // Without Redis this read returns null and the deployment silently serves two caches.
        (await storeB.GetAsync(key, cancellationToken)).Should().NotBeNull();

        await storeA.EvictByTagAsync(tag, cancellationToken);

        // Polled rather than asserted outright. Tag eviction in the Redis store is a write that the
        // reading side observes on its next lookup, and "next" is not the same instant on another
        // connection — a bare assertion here passes alone and fails under a loaded test run, which
        // is the worst kind of test to leave behind.
        await WaitUntilEvictedAsync(storeB, key, cancellationToken);
    }

    [Test]
    public async Task APublishOnOneInstanceIsServedByTheOther()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var database = $"cms_redis_{Guid.NewGuid():N}";

        await using (await sql.CreateDatabaseAsync(database, cancellationToken)) { }

        await using var instanceA = Instance(database);
        await using var instanceB = Instance(database);

        await using var scopeA = instanceA.Services.CreateAsyncScope();

        var template = await UseTemplateAsync(scopeA.ServiceProvider, cancellationToken);
        var page = await CreatePublishedPageAsync(scopeA.ServiceProvider, template, "Pricing", "First words", cancellationToken);

        await DrainAsync(instanceA, cancellationToken);
        await DrainAsync(instanceB, cancellationToken);

        using var clientB = instanceB.CreateClient();

        (await clientB.GetStringAsync("/pricing", cancellationToken)).Should().Contain("First words");

        await using (var scope = instanceA.Services.CreateAsyncScope())
        {
            await SaveDraftAsync(scope.ServiceProvider, page, "Second words", cancellationToken);

            (await scope.ServiceProvider.GetRequiredService<IPublishingService>()
                .PublishAsync(page, true, cancellationToken)).IsSuccess.Should().BeTrue();
        }

        // Instance A committed the publish; both instances' pollers pick the message up, and B's
        // request is answered from a cache that no longer holds the old page. Acceptance criterion
        // P8 #7.
        await DrainAsync(instanceA, cancellationToken);
        await DrainAsync(instanceB, cancellationToken);

        (await clientB.GetStringAsync("/pricing", cancellationToken)).Should().Contain("Second words");
    }

    /// <summary>Waits for a key to disappear from a store, up to a few seconds.</summary>
    private static async Task WaitUntilEvictedAsync(
        IOutputCacheStore store,
        string key,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await store.GetAsync(key, cancellationToken) is null) return;

            await Task.Delay(50, cancellationToken);
        }

        (await store.GetAsync(key, cancellationToken)).Should().BeNull();
    }

    private static async Task DrainAsync(CmsApplicationFactory instance, CancellationToken cancellationToken)
    {
        await using var scope = instance.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);
    }

    /// <summary>
    /// One host over the shared database and the shared Redis.
    /// </summary>
    /// <remarks>
    /// The two identity services are replaced for the reason <c>PageWorkbench</c> gives: outside a
    /// request the real ones answer "no permissions" and "user 0", and this suite arranges content
    /// through the services directly.
    /// </remarks>
    private CmsApplicationFactory Instance(string database) =>
        new(sql.ConnectionStringFor(database))
        {
            Settings = new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{Constants.OutputCacheConnectionString}"] = redis.ConnectionString,
            },
            ConfigureServices = (services, _) =>
            {
                services.RemoveAll<ICmsAuthorization>();
                services.AddSingleton<ICmsAuthorization>(StubAuthorization.Everything);
                services.RemoveAll<IUserService>();
                services.AddScoped<IUserService, StubUserService>();
            },
        };

    private static async Task<Template> UseTemplateAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<Data.Models.ApplicationDbContext>();

        var template = await context.Templates
            .Include(candidate => candidate.Zones)
            .Include(candidate => candidate.Revisions)
            .FirstAsync(candidate => candidate.Key == TemplateKey, cancellationToken);

        template.Zones.Add(new Zone
        {
            Key = "kicker",
            Name = "Kicker",
            FieldTypeKey = FieldTypeKeys.PlainText,
        });

        var revision = template.Revisions.Single(candidate => candidate.RevisionNumber == 1);
        revision.ZoneSnapshotJson = Core.Content.Schema.ContentSchemaSnapshot.WriteZones(template.Zones);

        await context.SaveChangesAsync(cancellationToken);

        return template;
    }

    private static async Task<int> CreatePublishedPageAsync(
        IServiceProvider services,
        Template template,
        string title,
        string text,
        CancellationToken cancellationToken)
    {
        var created = await services.GetRequiredService<IPageService>().CreateAsync(
            new CreatePageRequest(template.Id, title, null),
            cancellationToken);

        created.IsSuccess.Should().BeTrue();

        var pageId = created.Value!.Summary.Id;

        await SaveDraftAsync(services, pageId, text, cancellationToken);

        (await services.GetRequiredService<IPublishingService>()
            .PublishAsync(pageId, true, cancellationToken)).IsSuccess.Should().BeTrue();

        return pageId;
    }

    private static async Task SaveDraftAsync(
        IServiceProvider services,
        int pageId,
        string text,
        CancellationToken cancellationToken) =>
        (await services.GetRequiredService<IDraftService>().SaveAsync(
            pageId,
            new SaveDraftRequest(Payload(text), null),
            cancellationToken)).IsSuccess.Should().BeTrue();

    private static string Payload(string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{TemplateKey}}", "templateRevision": 1,
          "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
        """;
}
