namespace ContentManagementSystem.Core.Routing;

/// <summary>What the router decided a URL means.</summary>
public enum RouteResolutionKind
{
    /// <summary>Nothing serves this URL.</summary>
    NotFound = 0,

    /// <summary>A published page serves it.</summary>
    Page = 1,

    /// <summary>A redirect claims it.</summary>
    Redirect = 2,
}

/// <summary>
/// The answer to "what is at this URL".
/// </summary>
/// <param name="Kind">Which of the three answers this is.</param>
/// <param name="PageId">The page to render, when <see cref="Kind"/> is <see cref="RouteResolutionKind.Page"/>.</param>
/// <param name="TargetUrl">Where to send the visitor, when this is a redirect.</param>
/// <param name="StatusCode">The redirect status, when this is a redirect.</param>
/// <param name="RedirectId">Identity of the redirect followed, so its hit can be counted.</param>
/// <param name="CanonicalUrl">
/// The normalized form of the requested URL. Set when the request reached the right page by a
/// non-canonical spelling — a trailing slash, mixed case — so the endpoint can 301 to the canonical
/// one rather than serving the same content at two addresses.
/// </param>
public sealed record RouteResolution(
    RouteResolutionKind Kind,
    int PageId = 0,
    string? TargetUrl = null,
    short StatusCode = 0,
    int RedirectId = 0,
    string? CanonicalUrl = null)
{
    /// <summary>Nothing serves the URL.</summary>
    public static RouteResolution NotFound { get; } = new(RouteResolutionKind.NotFound);
}

/// <summary>
/// Turns a requested URL into a page, a redirect, or a 404.
/// </summary>
/// <remarks>
/// The order of the three lookups is the whole content of this interface, and it is a decision
/// rather than an implementation detail:
/// <list type="number">
/// <item>a published route, so a <strong>live page always wins</strong>;</item>
/// <item>a canonical-form correction, so <c>/About/</c> reaches <c>/about</c> with a 301;</item>
/// <item>a redirect.</item>
/// </list>
/// Putting redirects last is what makes it possible to retire a page and later reuse its URL for new
/// content (spec section 10.5). With the order reversed, the redirect the retirement created would
/// outrank the new page forever, and nothing would report why the page could not be reached.
/// </remarks>
public interface IRouteResolver
{
    /// <summary>Resolves a requested URL.</summary>
    /// <param name="url">The request path, as it arrived.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    Task<RouteResolution> ResolveAsync(string? url, CancellationToken cancellationToken = default);
}
