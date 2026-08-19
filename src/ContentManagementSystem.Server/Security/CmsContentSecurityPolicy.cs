using ContentManagementSystem.Core.Security;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// The three policy strings of spec section 20.5, built once at startup (task P9-01).
/// </summary>
/// <param name="sanitization">
/// The sanitization options, read for one thing: which hosts may be framed.
/// </param>
/// <param name="headers">The header options, read for the reporting endpoint.</param>
/// <remarks>
/// <strong><c>frame-src</c> comes from the sanitizer's allowlist rather than from a second list.</strong>
/// <see cref="SanitizationOptions.AllowedIframeHosts"/> decides whether an authored <c>iframe</c>
/// survives being stored; this decides whether the browser then loads it. Two lists would drift, and
/// the drift has no symptom on the way in — the element is stored, the page renders, and the embed is
/// an empty box. One list, two enforcement points.
/// <para>
/// Built at startup and held. A policy is a few hundred bytes of concatenation and it is on the path
/// of every response, including the cached ones.
/// </para>
/// </remarks>
public sealed class CmsContentSecurityPolicy(
    IOptions<SanitizationOptions> sanitization,
    IOptions<CmsSecurityHeaderOptions> headers)
{
    /// <summary>Everything in the backoffice policy before the script nonce.</summary>
    private const string BackofficeHead = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' 'nonce-";

    /// <summary>Everything between the script nonce and the style nonce.</summary>
    private const string BackofficeMiddle = "'; style-src 'self' 'nonce-";

    private readonly string _public = BuildPublic(sanitization.Value, headers.Value);

    private readonly string _preview = BuildPublic(sanitization.Value, headers.Value)
        .Replace("frame-ancestors 'none'", "frame-ancestors 'self'", StringComparison.Ordinal);

    private readonly string _backofficeTail = BuildBackofficeTail(sanitization.Value, headers.Value);

    /// <summary>
    /// The policy for a profile.
    /// </summary>
    /// <param name="profile">The profile the endpoint asked for.</param>
    /// <param name="nonce">
    /// The request's nonce, read only when <paramref name="profile"/> is
    /// <see cref="CmsCspProfile.Backoffice"/> — which is what keeps every public response from
    /// generating one it has no use for.
    /// </param>
    /// <returns>The header value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="nonce"/> is null.</exception>
    /// <remarks>
    /// The same nonce appears twice, for scripts and for styles. One value serves both because it is
    /// one request; a second nonce would be a second thing to plumb through to the same meta tag and
    /// a second thing to get wrong.
    /// </remarks>
    public string For(CmsCspProfile profile, ICspNonce nonce)
    {
        ArgumentNullException.ThrowIfNull(nonce);

        return profile switch
        {
            CmsCspProfile.Backoffice =>
                string.Concat(BackofficeHead, nonce.Value, BackofficeMiddle, nonce.Value, _backofficeTail),
            CmsCspProfile.Preview => _preview,
            _ => _public,
        };
    }

    /// <summary>
    /// Spec section 20.5's public policy, plus the three directives it implies.
    /// </summary>
    /// <remarks>
    /// The additions are <c>frame-src</c> — which section 20.5 asks for by name, as the thing the
    /// <c>embed</c> block extends — <c>connect-src</c>, and <c>style-src-attr</c>.
    /// <para>
    /// <strong>There is no nonce here.</strong> A public response is cached and replayed to everyone
    /// (task P8-06), so a per-request nonce in one would become a long-lived constant — precisely
    /// what a nonce may not be. <c>'self'</c> alone is strictly stronger, because the public document
    /// has no inline script of its own: the JSON-LD blocks are <c>application/ld+json</c>, a data
    /// block the HTML parser never executes and CSP therefore never consults. See ADR-0026.
    /// </para>
    /// <para>
    /// <c>style-src-attr 'unsafe-inline'</c> is the one relaxation, and it is what makes authored
    /// content render as it was written: <c>style</c> is an allowed attribute under the
    /// <c>Extended</c> and <c>Developer</c> profiles. It is narrow — attributes only, not
    /// <c>&lt;style&gt;</c> elements and not stylesheets — and the sanitizer has already reduced what
    /// an attribute may say to <see cref="SanitizationPolicy.AllowedCssProperties"/>, which holds
    /// nothing that can position an element, cover the page, or fetch a URL.
    /// </para>
    /// </remarks>
    private static string BuildPublic(SanitizationOptions sanitization, CmsSecurityHeaderOptions headers) =>
        Join(
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self'",
            "style-src-attr 'unsafe-inline'",
            "img-src 'self' data: https:",
            "font-src 'self'",
            "connect-src 'self'",
            FrameSrc(sanitization),
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "object-src 'none'",
            ReportUri(headers));

    /// <summary>
    /// The backoffice policy from the closing quote of the style nonce onwards.
    /// </summary>
    /// <remarks>
    /// <c>'wasm-unsafe-eval'</c>, in the head, is what the Blazor WebAssembly runtime requires and the
    /// whole of what it requires; <c>'unsafe-eval'</c> is not needed and is not here. The nonces
    /// around this cover the import map Blazor renders as an inline <c>&lt;script&gt;</c> and the
    /// <c>&lt;style&gt;</c> element CodeMirror injects at runtime.
    /// <para>
    /// <c>style-src-attr 'unsafe-inline'</c> is a change from ADR-0013, which recorded that the
    /// backoffice needed no such relaxation. That was true of Quill and CodeMirror and is not true of
    /// this application's own components: six of them position with a computed <c>style</c>
    /// attribute — the tree's depth indent, the context menu's coordinates, the shell's pane
    /// geometry, a colour swatch, a page list's indent, and an upload's progress bar — and CSP offers
    /// no nonce for a style attribute. ADR-0026 records the change.
    /// </para>
    /// <para>
    /// <c>frame-ancestors 'self'</c> is spec section 20.5's, for the v2 in-context editor.
    /// <c>frame-src</c> carries <c>'self'</c> for the preview pane and the embed hosts because the
    /// editing canvas renders the same authored content the public page does.
    /// </para>
    /// </remarks>
    private static string BuildBackofficeTail(
        SanitizationOptions sanitization,
        CmsSecurityHeaderOptions headers) =>
        "'; " + Join(
            "style-src-attr 'unsafe-inline'",
            "img-src 'self' data: https:",
            "font-src 'self'",
            "connect-src 'self'",
            FrameSrc(sanitization, self: true),
            "frame-ancestors 'self'",
            "base-uri 'self'",
            "form-action 'self'",
            "object-src 'none'",
            ReportUri(headers));

    /// <summary>
    /// The sources an <c>iframe</c> may load, drawn from the sanitizer's host allowlist.
    /// </summary>
    /// <param name="sanitization">The sanitization options.</param>
    /// <param name="self">Whether this origin is also a permitted frame source.</param>
    /// <returns>The directive.</returns>
    /// <remarks>
    /// <c>https</c> only, matching <c>SanitizationService.IsPermittedFrame</c>: an embed served over
    /// HTTP is blocked as mixed content before the policy is consulted, so permitting it here would
    /// widen the policy for something that cannot load anyway. An empty allowlist produces
    /// <c>'none'</c> rather than an omitted directive, so a deployment that has cleared the list gets
    /// the refusal it asked for rather than a fall back to <c>default-src</c>.
    /// </remarks>
    private static string FrameSrc(SanitizationOptions sanitization, bool self = false)
    {
        string[] sources =
        [
            .. self ? new[] { "'self'" } : [],
            .. sanitization.AllowedIframeHosts
                .OrderBy(host => host, StringComparer.Ordinal)
                .Select(host => $"https://{host}"),
        ];

        return sources.Length == 0
            ? "frame-src 'none'"
            : $"frame-src {string.Join(' ', sources)}";
    }

    private static string ReportUri(CmsSecurityHeaderOptions headers) =>
        string.IsNullOrWhiteSpace(headers.ReportUri) ? string.Empty : $"report-uri {headers.ReportUri}";

    private static string Join(params string[] directives) =>
        string.Join("; ", directives.Where(directive => directive.Length > 0));
}
