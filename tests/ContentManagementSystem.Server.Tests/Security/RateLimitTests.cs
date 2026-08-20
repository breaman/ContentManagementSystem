using System.Net;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Security;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Security;

/// <summary>
/// The rate limits of spec section 20.6, as the pipeline applies them (task P9-03).
/// </summary>
/// <remarks>
/// Two kinds of assertion, and the second is the one worth having. Driving a limiter to refusal over
/// HTTP proves the policy is attached and that it counts; asserting <em>which</em> endpoints carry
/// which policy proves the parts that are silently wrong when they are wrong — a credential route
/// misspelled in a list, a write endpoint added outside the group, a limit put on a read.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class RateLimitTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "article";

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task EveryCredentialRouteNamesAnEndpointThatExists()
    {
        // The convention matches on route text, so a renamed or misspelled page limits nothing and
        // says nothing. This is the only thing that notices.
        var routes = _bench.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var credential in CmsRateLimits.CredentialRoutes)
        {
            routes.Should().Contain(credential);
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task TheCredentialLimitIsOnTheSignInPagesAndNowhereElse()
    {
        var limited = EndpointsUnder(CmsRateLimits.Credentials);

        limited.Select(Path).Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().BeEquivalentTo(CmsRateLimits.CredentialRoutes);

        await Task.CompletedTask;
    }

    [Test]
    public async Task TheApiGroupCarriesTheWriteLimitAndTheUploadRoutesOverrideIt()
    {
        var api = EndpointsUnder(CmsRateLimits.ApiWrite);
        var uploads = EndpointsUnder(CmsRateLimits.Upload);

        // Every API route is under the group's policy except the three that begin an upload, which
        // name a tighter one. The more specific metadata is the one the limiter reads.
        api.Should().NotBeEmpty()
            .And.OnlyContain(route => route.Contains("/api/cms/v1", StringComparison.Ordinal));

        uploads.Should().BeEquivalentTo(
        [
            "POST /api/cms/v1/media/",
            "POST /api/cms/v1/media/uploads",
            "POST /api/cms/v1/media/{id:int}/replace",
        ]);

        // The listing GET shares a route with the single-shot upload POST and keeps the group's
        // policy, which is the reason these are compared by method as well as by path.
        api.Should().Contain("GET /api/cms/v1/media/").And.NotContain("POST /api/cms/v1/media/");

        await Task.CompletedTask;
    }

    [Test]
    public async Task DeliveryAndMediaAreLimitedAndTheFrameworkPathsAreNot()
    {
        EndpointsUnder(CmsRateLimits.PublicPages).Select(Path)
            .Should().BeEquivalentTo(["/{**slug}"]);

        EndpointsUnder(CmsRateLimits.MediaDelivery).Select(Path).Should().BeEquivalentTo(
        [
            "/media/{id:int}/{size}/{mode}/{name}",
            "/media/{id:int}/file/{name}",
        ]);

        // The point of not using a global limiter: a cold WebAssembly load is forty asset requests
        // and none of them spends a visitor's page budget.
        EndpointsUnder(CmsRateLimits.PublicPages)
            .Should().NotContain(route => route.Contains("_framework", StringComparison.Ordinal));

        await Task.CompletedTask;
    }

    [Test]
    public async Task ASixthSignInAttemptIsRefusedWithARetryAfter()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient(followRedirects: false);

        // Reads are exempt: a single failed sign-in renders the form twice, so counting the GET would
        // put the real budget at two attempts rather than five.
        for (var i = 0; i < CmsRateLimits.CredentialAttemptsPerWindow + 2; i++)
        {
            using var page = await client.GetAsync("/Account/Login", cancellationToken);

            page.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        for (var attempt = 1; attempt <= CmsRateLimits.CredentialAttemptsPerWindow; attempt++)
        {
            using var response = await PostLoginAsync(client, cancellationToken);

            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, $"attempt {attempt} is within budget");
        }

        using var refused = await PostLoginAsync(client, cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.Should().NotBeNull();
    }

    [Test]
    public async Task APublicPageIsNotRefusedWithinItsBudget()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await PublishedPageAsync("Busy", "Read often", cancellationToken);

        using var client = _bench.CreateClient();

        // Not a proof of the ceiling — six hundred requests is a slow test for a number the policy
        // already states. What this rules out is the mistake that matters: a limit accidentally set
        // low enough that ordinary reading trips it.
        for (var i = 0; i < 30; i++)
        {
            using var response = await client.GetAsync("/busy", cancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task AConfiguredPublicBudgetIsTheOneTheLimiterEnforces()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        // Configurable at all because a load test cannot run against the default: NFR-9 asks for
        // five thousand requests a second and the public budget is ten (task P9-13). Five here so
        // the test can reach the ceiling in five requests rather than six hundred.
        await using var bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: cancellationToken,
            settings: new Dictionary<string, string?>
            {
                [$"{CmsRateLimitOptions.SectionName}:{nameof(CmsRateLimitOptions.PublicPagesPerMinute)}"] = "5",
            });

        var template = await bench.UseTemplateAsync(
            "configured-budget",
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

        var page = await bench.AddPageAsync(template, "Throttled", cancellationToken);

        await bench.Resolve<IPublishingService>().PublishAsync(page.Summary.Id, cancellationToken: cancellationToken);

        bench.Context.ChangeTracker.Clear();

        using var client = bench.CreateClient();

        for (var request = 1; request <= 5; request++)
        {
            using var response = await client.GetAsync("/throttled", cancellationToken);

            response.StatusCode.Should().NotBe(
                HttpStatusCode.TooManyRequests,
                $"request {request} is inside the configured budget of five");
        }

        using var refused = await client.GetAsync("/throttled", cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task ABudgetThatWouldRefuseEveryRequestIsRejectedAtStartup(int configured)
    {
        var options = new CmsRateLimitOptions { PublicPagesPerMinute = configured };

        // Zero is not "no limit", it is "refuse everything", and a negative one throws from inside
        // the limiter on the first request rather than when the deployment starts.
        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(CmsRateLimitOptions.PublicPagesPerMinute)}*");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Endpoints whose metadata names a policy, as "VERB /route".
    /// </summary>
    /// <param name="policy">The policy name.</param>
    /// <returns>The endpoints, one entry per verb.</returns>
    /// <remarks>
    /// By verb as well as by path, because <c>/api/cms/v1/media/</c> is a listing and an upload
    /// depending on the method, and they are deliberately under different policies.
    /// </remarks>
    private string[] EndpointsUnder(string policy) =>
        _bench.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == policy)
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(method => $"{method} /{endpoint.RoutePattern.RawText?.TrimStart('/')}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The path half of an "VERB /route" entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The route.</returns>
    private static string Path(string entry) => entry[(entry.IndexOf(' ', StringComparison.Ordinal) + 1)..];

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        CancellationToken cancellationToken) =>
        await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                // The form name Blazor dispatches static SSR posts by. Without it the page has no
                // handler for the request and answers 404, which would make this assert nothing.
                ["_handler"] = "login",
                ["Input.Email"] = "nobody@example.test",
                ["Input.Password"] = "not-the-password",
            }),
            cancellationToken);

    private async Task PublishedPageAsync(string title, string text, CancellationToken cancellationToken)
    {
        var template = await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(
                $$"""
                { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
                  "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
                """,
                null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue();
        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue();
        _bench.Context.ChangeTracker.Clear();
    }
}
