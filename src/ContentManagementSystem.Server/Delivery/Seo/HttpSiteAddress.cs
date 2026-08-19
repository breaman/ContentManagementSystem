using ContentManagementSystem.Core.Delivery.Seo;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Delivery.Seo;

/// <summary>
/// The site's absolute address, taken from configuration when it is set and from the request when
/// it is not (task P8-01).
/// </summary>
/// <param name="accessor">The request being served, when there is one.</param>
/// <param name="options">The configured public base URL.</param>
/// <remarks>
/// Configuration wins deliberately. Behind a proxy or a CDN the request's own host is an internal
/// address, and a canonical link or a sitemap entry naming it tells a crawler the site lives
/// somewhere nobody can reach — a mistake that is invisible until traffic disappears.
/// <para>
/// The request is the fallback rather than the source because it is not always there: the sitemap
/// can be warmed by a background job, and a job has no request to read a host from. A deployment
/// that never configures one still works for every ordinary page view, which is what keeps a
/// developer's first run from needing a setting.
/// </para>
/// </remarks>
public sealed class HttpSiteAddress(IHttpContextAccessor accessor, IOptions<SeoOptions> options)
    : ISiteAddress
{
    /// <summary>The address used when nothing is configured and no request is in flight.</summary>
    /// <remarks>
    /// A last resort that produces syntactically valid absolute URLs rather than an exception. A
    /// crawler will never see it: a page is served in a request, and this is reached only by work
    /// running without one on a host that configured nothing.
    /// </remarks>
    public static Uri Fallback { get; } = new("http://localhost/");

    /// <inheritdoc />
    public Uri BaseUri
    {
        get
        {
            if (Configured is { } configured) return configured;

            var request = accessor.HttpContext?.Request;

            if (request is null || !request.Host.HasValue) return Fallback;

            // Built rather than concatenated: a UriBuilder is what gets the default port right —
            // an explicit :443 in a canonical URL is a second address for the same page as far as a
            // crawler is concerned.
            var builder = new UriBuilder(request.Scheme, request.Host.Host)
            {
                Path = request.PathBase.HasValue ? request.PathBase.Value + "/" : "/",
                Port = request.Host.Port ?? -1,
            };

            return builder.Uri;
        }
    }

    private Uri? Configured
    {
        get
        {
            var configured = options.Value.PublicBaseUrl;

            if (string.IsNullOrWhiteSpace(configured)) return null;

            // A trailing slash is required for relative resolution: Uri(base, "about") against
            // "https://example.com/site" yields "https://example.com/about", silently dropping the
            // path base a deployment configured.
            return Uri.TryCreate(
                configured.EndsWith('/') ? configured : configured + "/",
                UriKind.Absolute,
                out var parsed)
                ? parsed
                : null;
        }
    }
}
