using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Server.Security;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Tests.Security;

/// <summary>
/// The three policy strings of spec section 20.5 (task P9-01).
/// </summary>
/// <remarks>
/// Restated here rather than derived from <see cref="CmsContentSecurityPolicy"/>, for the reason the
/// XSS corpus restates its forbidden list: a policy widened by mistake would otherwise widen the test
/// that is supposed to catch it. The assertions below say what may not appear at all —
/// <c>unsafe-inline</c> in a fetch directive, <c>unsafe-eval</c>, a wildcard host — and those are the
/// ones that matter.
/// </remarks>
public class ContentSecurityPolicyTests
{
    private static readonly ICspNonce Nonce = new StubNonce("test-nonce");

    [Test]
    [Arguments(CmsCspProfile.Public)]
    [Arguments(CmsCspProfile.Preview)]
    [Arguments(CmsCspProfile.Backoffice)]
    public async Task NoProfilePermitsInlineScriptEvalOrAWildcardHost(CmsCspProfile profile)
    {
        var policy = Build().For(profile, Nonce);

        // 'unsafe-inline' appears in exactly one directive, style-src-attr, and the test below pins
        // it there. Anywhere else it is the thing a CSP exists to refuse.
        policy.Replace("style-src-attr 'unsafe-inline'", string.Empty, StringComparison.Ordinal)
            .Should().NotContain("'unsafe-inline'");

        policy.Should().NotContain("'unsafe-eval'")
            .And.NotContain("'unsafe-hashes'")
            .And.NotContain("*")
            .And.NotContain("http://");

        policy.Should().Contain("object-src 'none'")
            .And.Contain("base-uri 'self'")
            .And.Contain("form-action 'self'");

        await Task.CompletedTask;
    }

    [Test]
    public async Task ThePublicPolicyCarriesNoNonceAndRefusesFraming()
    {
        var policy = Build().For(CmsCspProfile.Public, Nonce);

        // The point of ADR-0026: a public response is cached and replayed, so a per-request nonce in
        // one is a constant. 'self' alone is what the document actually needs.
        policy.Should().NotContain("nonce-")
            .And.Contain("script-src 'self';")
            .And.Contain("frame-ancestors 'none'")
            .And.Contain("default-src 'self'");

        await Task.CompletedTask;
    }

    [Test]
    public async Task PreviewDiffersFromPublicOnlyInFrameAncestors()
    {
        var policy = Build();

        var preview = policy.For(CmsCspProfile.Preview, Nonce);
        var @public = policy.For(CmsCspProfile.Public, Nonce);

        preview.Should().Contain("frame-ancestors 'self'");
        preview.Replace("frame-ancestors 'self'", "frame-ancestors 'none'", StringComparison.Ordinal)
            .Should().Be(@public);

        await Task.CompletedTask;
    }

    [Test]
    public async Task TheBackofficePolicyCarriesTheRuntimeAndTheRequestNonce()
    {
        var policy = Build().For(CmsCspProfile.Backoffice, Nonce);

        // 'wasm-unsafe-eval' is what the Blazor WebAssembly runtime requires; the nonce covers the
        // import map and CodeMirror's injected theme, and appears once for each.
        policy.Should().Contain("'wasm-unsafe-eval'")
            .And.Contain("script-src 'self' 'wasm-unsafe-eval' 'nonce-test-nonce'")
            .And.Contain("style-src 'self' 'nonce-test-nonce'")
            .And.Contain("frame-ancestors 'self'");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FrameSrcIsTheSanitizersHostAllowlistAndNothingElse()
    {
        var sanitization = new SanitizationOptions();
        sanitization.AllowedIframeHosts.Clear();
        sanitization.AllowedIframeHosts.Add("player.vimeo.com");

        var policy = Build(sanitization).For(CmsCspProfile.Public, Nonce);

        // One list, two enforcement points: the sanitizer decides whether the element survives being
        // stored, this decides whether the browser then loads it. A host in one and not the other
        // produces an embed that is stored and renders an empty box.
        policy.Should().Contain("frame-src https://player.vimeo.com;")
            .And.NotContain("youtube");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AnEmptyHostAllowlistRefusesFramingRatherThanFallingBack()
    {
        var sanitization = new SanitizationOptions();
        sanitization.AllowedIframeHosts.Clear();

        var policy = Build(sanitization).For(CmsCspProfile.Public, Nonce);

        // An omitted frame-src falls back to default-src 'self', which would permit framing this
        // origin — a wider policy than the deployment asked for by clearing the list.
        policy.Should().Contain("frame-src 'none'");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AReportingEndpointIsEmittedOnlyWhenOneIsConfigured()
    {
        Build().For(CmsCspProfile.Public, Nonce).Should().NotContain("report-uri");

        Build(headers: new CmsSecurityHeaderOptions { ReportUri = "/csp-reports" })
            .For(CmsCspProfile.Public, Nonce)
            .Should().EndWith("report-uri /csp-reports");

        await Task.CompletedTask;
    }

    private static CmsContentSecurityPolicy Build(
        SanitizationOptions? sanitization = null,
        CmsSecurityHeaderOptions? headers = null) =>
        new(
            Options.Create(sanitization ?? new SanitizationOptions()),
            Options.Create(headers ?? new CmsSecurityHeaderOptions()));

    private sealed class StubNonce(string value) : ICspNonce
    {
        public string Value => value;
    }
}
