using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// The response security headers of spec section 20.5 (tasks P9-01 and P9-02).
/// </summary>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="policy">The three content security policies.</param>
/// <param name="options">Whether the policy is enforced, reported, or off.</param>
/// <remarks>
/// Set on the way <em>in</em> rather than from a response callback, and that is the ordering
/// decision: an endpoint that has already begun writing cannot have headers added to it, and the
/// three surfaces most in need of these headers — a media response, a 404, an exception page — are
/// exactly the ones that write early.
/// <para>
/// Placed after routing so the endpoint's <see cref="CmsCspProfileMetadata"/> is visible, and before
/// output caching so a cache hit still gets the header. It is the same header either way for a
/// public response, because the public policy has no nonce in it; if it ever grows one, this ordering
/// is what stops a cached page from serving a stale one (ADR-0026).
/// </para>
/// </remarks>
public sealed class CmsSecurityHeadersMiddleware(
    RequestDelegate next,
    CmsContentSecurityPolicy policy,
    IOptions<CmsSecurityHeaderOptions> options)
{
    /// <summary>Name of the <c>Referrer-Policy</c> header, which <c>HeaderNames</c> has no constant for.</summary>
    public const string ReferrerPolicyHeader = "Referrer-Policy";

    /// <summary>Name of the <c>Permissions-Policy</c> header.</summary>
    public const string PermissionsPolicyHeader = "Permissions-Policy";

    /// <summary>
    /// <c>Referrer-Policy</c> per spec section 20.5.
    /// </summary>
    /// <remarks>
    /// The origin and no more when leaving this site, the full URL within it. A CMS URL is content —
    /// <c>/hr/redundancy-consultation-2026</c> is a fact about the organisation before anyone opens
    /// it — so the path does not travel to whatever an editor linked to.
    /// </remarks>
    public const string ReferrerPolicy = "strict-origin-when-cross-origin";

    /// <summary>
    /// The minimal <c>Permissions-Policy</c> of spec section 20.5.
    /// </summary>
    /// <remarks>
    /// Everything off except the three the application actually uses.
    /// <c>publickey-credentials-get</c> and <c>publickey-credentials-create</c> are <c>self</c>
    /// because Identity's passkey support is already wired up and a blanket denial would turn signing
    /// in with one into a <c>NotAllowedError</c> nobody would attribute to a header; <c>fullscreen</c>
    /// is <c>self</c> for the preview pane's device widths. Nothing on this list is granted to a
    /// third party, which is what <c>()</c> rather than <c>*</c> says.
    /// </remarks>
    public const string PermissionsPolicy =
        "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), " +
        "fullscreen=(self), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), " +
        "midi=(), payment=(), picture-in-picture=(), publickey-credentials-create=(self), " +
        "publickey-credentials-get=(self), screen-wake-lock=(), serial=(), usb=(), " +
        "xr-spatial-tracking=()";

    private readonly CmsSecurityHeaderOptions _options = options.Value;

    /// <summary>
    /// Applies the headers and continues.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the rest of the pipeline has.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;

        // Assignment rather than TryAdd for all three. An endpoint that has set one of these itself
        // — the media endpoint sets nosniff on every response it writes (spec section 20.7) — has set
        // it to the same value, and a differing one would be a route quietly opting out of a
        // site-wide header.
        headers.XContentTypeOptions = "nosniff";
        headers[ReferrerPolicyHeader] = ReferrerPolicy;
        headers[PermissionsPolicyHeader] = PermissionsPolicy;

        if (_options.ContentSecurityPolicyEnabled)
        {
            var profile = context.GetEndpoint()?.Metadata.GetMetadata<CmsCspProfileMetadata>()?.Profile
                          ?? CmsCspProfile.Public;

            var nonce = context.RequestServices.GetRequiredService<ICspNonce>();

            headers[_options.ReportOnly
                ? HeaderNames.ContentSecurityPolicyReportOnly
                : HeaderNames.ContentSecurityPolicy] = policy.For(profile, nonce);
        }

        return next(context);
    }
}
