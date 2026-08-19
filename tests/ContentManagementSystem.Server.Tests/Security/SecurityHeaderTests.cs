using System.Net;
using System.Text.RegularExpressions;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Security;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Security;

/// <summary>
/// The security headers as a browser receives them (tasks P9-01, P9-02).
/// </summary>
/// <remarks>
/// The policy strings themselves are covered by <see cref="ContentSecurityPolicyTests"/>. What this
/// suite is for is everything between the string and the response: that the profile is selected from
/// endpoint metadata rather than guessed from a path, that a page served from the output cache still
/// carries a header, and — the one that has no other check — that the nonce in the backoffice header
/// is the same value the host page put in its meta tag and on its import map. Three copies of one
/// number, and the failure when they disagree is a backoffice that renders and does nothing.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class SecurityHeaderTests(SqlServerFixture fixture)
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
    public async Task APublicPageCarriesTheStrictPolicyAndTheThreeCompanionHeaders()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await PublishedPageAsync("Pricing", "Our best plans yet", cancellationToken);

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/pricing", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        Csp(response).Should().Contain("frame-ancestors 'none'")
            .And.Contain("script-src 'self';")
            .And.NotContain("nonce-")
            .And.NotContain("wasm-unsafe-eval");

        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, CmsSecurityHeadersMiddleware.ReferrerPolicyHeader)
            .Should().Be(CmsSecurityHeadersMiddleware.ReferrerPolicy);
        Header(response, CmsSecurityHeadersMiddleware.PermissionsPolicyHeader)
            .Should().Be(CmsSecurityHeadersMiddleware.PermissionsPolicy);
    }

    [Test]
    public async Task PasskeysAndTheDeviceFrameAreTheOnlyThingsPermissionsPolicyGrants()
    {
        // A blanket denial would take Identity's passkey support with it, and the symptom is a
        // NotAllowedError from the browser that nothing in this application logs.
        CmsSecurityHeadersMiddleware.PermissionsPolicy.Should()
            .Contain("publickey-credentials-get=(self)")
            .And.Contain("publickey-credentials-create=(self)")
            .And.Contain("fullscreen=(self)")
            .And.Contain("camera=()")
            .And.Contain("geolocation=()")
            .And.Contain("microphone=()");

        CmsSecurityHeadersMiddleware.PermissionsPolicy.Should().NotContain("*");

        await Task.CompletedTask;
    }

    [Test]
    public async Task TheBackofficeNonceInTheHeaderIsTheOneTheDocumentUses()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/account/login", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var policy = Csp(response);
        policy.Should().Contain("'wasm-unsafe-eval'").And.Contain("frame-ancestors 'self'");

        var nonce = Regex.Match(policy, @"script-src [^;]*'nonce-([^']+)'").Groups[1].Value;
        nonce.Should().NotBeEmpty();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // The meta tag is the contract with CodeMirror (ADR-0013); the import map is an inline
        // script Blazor renders, and script-src 'self' blocks it without this.
        html.Should().Contain($"""<meta name="csp-nonce" content="{nonce}" />""")
            .And.Contain($"""nonce="{nonce}""");

        // And it is a nonce rather than a constant: a second request gets a different one.
        using var second = await client.GetAsync("/account/login", cancellationToken);
        Csp(second).Should().NotContain(nonce);
    }

    [Test]
    public async Task ARouteThatSaysNothingIsServedUnderThePublicPolicy()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient();

        // robots.txt, a 404, and the API all reach here without asking for a profile, which is the
        // half of ADR-0026's design that matters: strict is what a route gets for saying nothing.
        foreach (var path in new[] { "/robots.txt", "/no-such-page", "/api/cms/pages" })
        {
            using var response = await client.GetAsync(path, cancellationToken);

            Csp(response).Should().Contain("frame-ancestors 'none'", $"{path} asked for no profile");
        }
    }

    [Test]
    public async Task ACachedPageStillCarriesAPolicy()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await PublishedPageAsync("Cached", "Served twice", cancellationToken);

        using var client = _bench.CreateClient();

        using var first = await client.GetAsync("/cached", cancellationToken);
        using var second = await client.GetAsync("/cached", cancellationToken);

        // The second is the one that matters: it is served out of the output cache, and the headers
        // it carries are written by middleware sitting in front of that cache rather than replayed
        // from whatever was recorded when the page rendered.
        Csp(second).Should().Be(Csp(first)).And.NotBeEmpty();
        Header(second, "X-Content-Type-Options").Should().Be("nosniff");
    }

    private static string Csp(HttpResponseMessage response) =>
        Header(response, "Content-Security-Policy");

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(' ', values) : string.Empty;

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

        _bench.Context.ChangeTracker.Clear();

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, text), null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue();
        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue();
        _bench.Context.ChangeTracker.Clear();

        return page;
    }

    private static string Payload(string templateKey, string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
          "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
        """;
}
