using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// A publish that fails mid-transaction, driven through the HTTP API (task P2-28).
/// </summary>
/// <remarks>
/// <c>PublishTransactionTests</c> already forces a failure at each step and asserts the rollback at
/// the service layer. This asserts the same property one layer out, and the difference is not
/// ceremony: the endpoint runs inside ASP.NET Core's request scope, with its own
/// <c>ApplicationDbContext</c>, its own <c>SaveChanges</c> from the audit interceptor, and a
/// connection the pipeline disposes on the way out. A transaction that rolls back correctly when a
/// test drives the service can still be committed in halves by a request that ends differently.
/// <para>
/// What the client sees matters as much as what the database keeps. An editor whose publish failed
/// must be told so — a 2xx over a rolled-back transaction produces a page everybody believes is live.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class PublishFaultInjectionApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private FailingSaveInterceptor _interceptor = null!;
    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _interceptor = new FailingSaveInterceptor();
        _factory = await CmsApplicationFactory.CreateAsync(
            fixture,
            TestContext.Current.CancellationToken,
            (services, connectionString) =>
            {
                // Re-registered rather than added to DI: EF resolves interceptors from the options
                // it was built with, and the application's registration was already made without
                // this one.
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseSqlServer(connectionString)
                    .AddInterceptors(_interceptor));
            });
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>
    /// One case per <c>SaveChanges</c> inside the publish transaction.
    /// </summary>
    /// <remarks>
    /// In order: insert the new version; archive the previous one and repoint the page; project the
    /// reference rows; and the placeholder for the cache-invalidation outbox row arriving in P8.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AFailedPublishAnswersWithAServerErrorAndChangesNothing(int failingStep)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, $"fault-{failingStep}", cancellationToken);
        var page = await CreatePageAsync(client, template, "Pricing", cancellationToken);
        var pageId = page.Summary.Id;

        (await FillZoneAsync(client, pageId, "body", "First", cancellationToken))
            .IsSuccessStatusCode.Should().BeTrue();

        // A first successful publish, so the failing one has a previous version to archive and a
        // page pointer to move. Failing on a page that was never live would not exercise step 2.
        var live = await client.PostAsJsonAsync(
            $"{Pages}/{pageId}/publish",
            new PublishPageRequest(true),
            cancellationToken);

        live.StatusCode.Should().Be(HttpStatusCode.OK);

        (await FillZoneAsync(client, pageId, "body", "Second", cancellationToken))
            .IsSuccessStatusCode.Should().BeTrue();

        var before = await StateAsync(client, pageId, cancellationToken);

        _interceptor.FailOnCall(failingStep);

        var attempt = await client.PostAsJsonAsync(
            $"{Pages}/{pageId}/publish",
            new PublishPageRequest(true),
            cancellationToken);

        _interceptor.Reset();

        // Not a 2xx and not a problem-details refusal either. This is not the editor being told the
        // page is unfit to publish; it is the server failing to do what it was asked.
        attempt.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var after = await StateAsync(client, pageId, cancellationToken);

        after.Should().BeEquivalentTo(before, "the whole publish rolls back or none of it does");
    }

    [Fact]
    public async Task APublishThatSucceedsThroughTheApiLeavesEveryStepApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "fault-control", cancellationToken);
        var page = await CreatePageAsync(client, template, "Pricing", cancellationToken);
        var pageId = page.Summary.Id;

        await FillZoneAsync(client, pageId, "body", "First", cancellationToken);

        var published = await client.PostAsJsonAsync(
            $"{Pages}/{pageId}/publish",
            new PublishPageRequest(true),
            cancellationToken);

        published.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await published.Content.ReadFromJsonAsync<PublishResult>(cancellationToken))!;
        var after = await StateAsync(client, pageId, cancellationToken);

        // The control case for the theory above. Without it, an interceptor that broke publishing
        // outright would make every roll-back assertion pass for the wrong reason.
        after.PublishedVersionNumber.Should().Be(result.VersionNumber);
        after.Versions.Should().HaveCount(2);
        after.Versions.Should().ContainSingle(version => version.IsPublished && version.Id == result.VersionId);
    }

    /// <summary>
    /// Everything a publish touches, read back the way a client reads it.
    /// </summary>
    /// <remarks>
    /// Through the API rather than against the database on purpose. What has to be true after a
    /// failed publish is that nobody can see a difference, and "nobody" here means the client — a
    /// row that rolled back but left a stale pointer in a response is still a broken page.
    /// </remarks>
    private static async Task<PageState> StateAsync(
        HttpClient client,
        int pageId,
        CancellationToken cancellationToken)
    {
        var page = (await client.GetFromJsonAsync<PageDetail>($"{Pages}/{pageId}", cancellationToken))!;

        var versions = (await client.GetFromJsonAsync<List<PageVersionSummary>>(
            $"{Pages}/{pageId}/versions",
            cancellationToken))!;

        var draft = (await client.GetFromJsonAsync<DraftState>(
            $"{Pages}/{pageId}/draft",
            cancellationToken))!;

        return new PageState(
            page.Summary.PublishedVersionNumber,
            page.Summary.Status,
            page.Summary.HasUnpublishedChanges,
            draft.ContentJson,
            [.. versions
                .OrderBy(version => version.VersionNumber)
                .Select(version => new VersionState(
                    version.Id,
                    version.VersionNumber,
                    version.Status,
                    version.IsDraft,
                    version.IsPublished))]);
    }

    private sealed record VersionState(
        int Id,
        int VersionNumber,
        string Status,
        bool IsDraft,
        bool IsPublished);

    private sealed record PageState(
        int? PublishedVersionNumber,
        string Status,
        bool HasUnpublishedChanges,
        string ContentJson,
        IReadOnlyList<VersionState> Versions);
}
