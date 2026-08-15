using System.Net;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// The catch-all does not shadow the rest of the application (task P3-15, risk R6).
/// </summary>
/// <remarks>
/// A terminal <c>/{**slug}</c> is the single highest-consequence route in the system: registered
/// wrongly, it swallows the API, the backoffice, the sign-in pages, and the health endpoints, and
/// every one of those failures looks like "the CMS returns a 404 page" rather than like a routing
/// bug.
/// <para>
/// These assert the <em>outcome</em> rather than the registration order, deliberately. Order is one
/// way to get this right and route precedence is another; what must not change is that these paths
/// reach the endpoints that own them, whatever anybody does to <c>Program.cs</c> later.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class RouteOrderingTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task TheManagementApiStillAnswersAsAnApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = _factory.CreateClientAs("Developer");
        using var response = await client.GetAsync($"{CmsApiEndpoints.BasePath}/templates", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The distinguishing assertion. Swallowed by the catch-all, this would still be a 200 — of
        // an HTML 404 page, which a JSON client would report as a parse error somewhere else
        // entirely.
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task AnApiPathThatDoesNotExistIs404FromTheApiAndNotAnHtmlPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = _factory.CreateClientAs("Developer");
        using var response = await client.GetAsync($"{CmsApiEndpoints.BasePath}/no-such-resource", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // /api is reserved, so the catch-all must not offer to serve a page there even when nothing
        // else does. An HTML body here would mean a content slug could impersonate an API resource.
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task TheHealthEndpointsAreNotShadowed(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(path, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Text, not markup. An orchestrator's readiness probe reading an HTML 404 page as "Healthy"
        // is the failure mode this rules out.
        (await response.Content.ReadAsStringAsync(cancellationToken)).Should().Be("Healthy");
    }

    [Theory]
    [InlineData("/admin/pages")]
    [InlineData("/admin/structure/templates")]
    public async Task TheBackofficeIsNotShadowed(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Signed in, because these pages carry [Authorize] and an anonymous request is refused
        // before routing has anything to say. The question here is which endpoint owns the path, and
        // only a caller who is allowed through it can answer that.
        using var client = _factory.CreateClientAs("Administrator");
        using var response = await client.GetAsync(path, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // Both documents are HTML, so the assertion is on which one arrived: the backoffice shell
        // carries the WebAssembly bootstrapper, and the CMS delivery document never does.
        html.Should().Contain("blazor.web.js");
        html.Should().NotContain("cms-delivery");
    }

    [Fact]
    public async Task TheSignInPageIsNotShadowed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/Account/Login", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        html.Should().Contain("blazor.web.js").And.NotContain("cms-delivery");
    }

    [Fact]
    public async Task AnUnmatchedPathUnderAReservedPrefixIsABare404AndNotTheSites404Page()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/admin/no-such-screen", cancellationToken);

        // The catch-all matches everything, so a path under a prefix the application owns still
        // reaches it. Serving the site's 404 page there would make content appear to be served from
        // a reserved prefix — one no page can ever be published at (spec section 10.3).
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(cancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheBlazorFrameworkFilesAreNotShadowed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/_framework/blazor.web.js", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().NotBe("text/html");
    }

    [Fact]
    public async Task AnOrdinaryContentUrlDoesReachTheCatchAll()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/some/page/nobody/published", cancellationToken);

        // The other half of the claim: everything above is reserved, and everything else is content.
        // Without this, a catch-all that had been removed entirely would pass every test above.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain("Page not found");
    }
}
