using System.Data.Common;
using System.Net;

using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// A connection that can be made to stop working, on demand.
/// </summary>
/// <remarks>
/// The chaos test needs a database that goes away while the process keeps running, and it must not
/// be the container: one SQL Server serves the whole session, and stopping it would take every other
/// suite with it. Refusing to open a connection is what a database being unreachable looks like from
/// inside the application, which is the only part of it delivery can observe.
/// </remarks>
public sealed class OutageInterceptor : DbConnectionInterceptor
{
    /// <summary>Whether the database is currently pretending to be gone.</summary>
    public bool IsDown { get; set; }

    /// <inheritdoc />
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result) =>
        IsDown ? throw Outage() : base.ConnectionOpening(connection, eventData, result);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default) =>
        IsDown
            ? throw Outage()
            : base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);

    private static InvalidOperationException Outage() =>
        new("The database is unreachable (simulated outage, task P9-17).");
}

/// <summary>
/// NFR-11: the public site keeps serving what it has cached while the backoffice is down
/// (task P9-17).
/// </summary>
/// <remarks>
/// "Backoffice outage" is a database outage here, and deliberately so. This is one process
/// ([ADR 0002](../../../docs/adr/0002-static-ssr-public-interactive-wasm-backoffice.md)), so there
/// is no separate backoffice tier to stop; what the requirement is actually about is whether serving
/// a page that has already been rendered depends on anything an editor's half of the system needs.
/// The answer has to be no, and the only way to find out is to take the dependency away.
/// <para>
/// The test asserts both halves, because either alone would be misleading: a cached page still
/// answers 200, <strong>and</strong> the health endpoint says the instance is unhealthy. A site that
/// kept serving and reported itself healthy would be hiding an outage; one that reported the outage
/// and stopped serving would have failed the requirement.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class OutageTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "outage";

    private readonly OutageInterceptor _outage = new();

    private PageWorkbench _bench = null!;

    private Template? _template;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: TestContext.Current!.Execution.CancellationToken,
            interceptor: _outage);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync()
    {
        // Down again on the way out and the factory's own disposal cannot open a connection either.
        _outage.IsDown = false;

        await _bench.DisposeAsync();
    }

    [Test]
    public async Task ACachedPageKeepsServingWhileTheDatabaseIsUnreachable()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishAsync("Cached", "Served from the cache", cancellationToken);
        await PublishAsync("Cold", "Never requested", cancellationToken);

        using var client = _bench.CreateClient();

        // Warm one and leave the other cold. The cold one is the control: without it, a test where
        // everything still answers would prove nothing about the cache.
        var warm = await client.GetStringAsync("/cached", cancellationToken);

        warm.Should().Contain("Served from the cache");

        _outage.IsDown = true;

        using var cached = await client.GetAsync("/cached", cancellationToken);

        cached.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "NFR-11: content already in the output cache is served without touching the database");

        (await cached.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain("Served from the cache");

        using var cold = await client.GetAsync("/cold", cancellationToken);

        ((int)cold.StatusCode).Should().BeGreaterThanOrEqualTo(
            500,
            "a page nothing has rendered yet cannot be produced without the database, and pretending " +
            "otherwise would mean serving something wrong");

        using var health = await client.GetAsync("/health", cancellationToken);

        health.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable,
            "the instance is unhealthy even while it is still serving; an outage the monitoring " +
            "cannot see is worse than one it can");

        // And it comes back on its own. Nothing restarts, nothing is invalidated: the cold page is
        // rendered the moment the database answers again.
        _outage.IsDown = false;

        using var recovered = await client.GetAsync("/cold", cancellationToken);

        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await recovered.Content.ReadAsStringAsync(cancellationToken)).Should().Contain("Never requested");
    }

    private async Task PublishAsync(string title, string text, CancellationToken cancellationToken)
    {
        // Created once. UseTemplateAsync adds the zones it is given, so asking twice for the same
        // template with the same zone is a duplicate key rather than an idempotent call.
        _template ??= await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

        var page = await _bench.AddPageAsync(_template, title, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var payload = $$"""
            { "schemaVersion": 1, "templateKey": "{{TemplateKey}}", "templateRevision": 1,
              "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
            """;

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(payload, null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(PageWorkbench.Because(saved));

        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        published.IsSuccess.Should().BeTrue(PageWorkbench.Because(published));

        _bench.Context.ChangeTracker.Clear();

        await using var scope = _bench.NewScope();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);
    }
}
