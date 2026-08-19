namespace ContentManagementSystem.Server.Security;

/// <summary>
/// Which of the three content security policies an endpoint is served under (task P9-01).
/// </summary>
/// <remarks>
/// The profile is endpoint metadata rather than a path prefix, for the reason the output cache is
/// opt-in per endpoint rather than a base policy with exclusions (task P8-06): a prefix list is a
/// thing somebody adding a route has to know about, and the failure when they do not is a page that
/// works. Here it would be the opposite — the strict policy is the default, so a new route that
/// needs a wider one fails loudly the first time somebody loads it.
/// </remarks>
public enum CmsCspProfile
{
    /// <summary>
    /// The public site, and everything that has not asked for anything else (spec section 20.5).
    /// </summary>
    /// <remarks>
    /// No nonce, no <c>unsafe-inline</c> for scripts, and <c>frame-ancestors 'none'</c>. This is
    /// what a delivery response, a media response, an API response, and a 404 are all served under.
    /// </remarks>
    Public = 0,

    /// <summary>
    /// Preview: the public policy, except that the document may be framed by this origin.
    /// </summary>
    /// <remarks>
    /// The preview chrome puts the rendered page in an <c>iframe</c> so a device width can be
    /// applied to it, and the editing canvas frames the same URL again. <c>frame-ancestors 'none'</c>
    /// blocks that even though both documents come from this origin, so preview needs its own
    /// profile — and it is the only difference, because preview renders authored content through
    /// exactly the components delivery does.
    /// </remarks>
    Preview = 1,

    /// <summary>
    /// The backoffice: the WebAssembly runtime, the editor bundles, and the import map.
    /// </summary>
    /// <remarks>
    /// Carries <c>'wasm-unsafe-eval'</c>, the per-request nonce of <c>ADR-0013</c>, and
    /// <c>frame-ancestors 'self'</c> for the v2 in-context editor.
    /// </remarks>
    Backoffice = 2,
}

/// <summary>
/// Endpoint metadata naming the policy profile a route is served under.
/// </summary>
/// <param name="Profile">The profile.</param>
/// <remarks>
/// Absent metadata means <see cref="CmsCspProfile.Public"/>. Nothing has to opt into the strict
/// policy, which is the half of this design that matters.
/// </remarks>
public sealed record CmsCspProfileMetadata(CmsCspProfile Profile);
