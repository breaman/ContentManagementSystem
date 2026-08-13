namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// How permissive the HTML allowlist is for one piece of authored markup (spec section 20.2).
/// </summary>
/// <remarks>
/// The profiles nest: every rule <see cref="Basic"/> enforces, the wider two enforce as well. Rules
/// that hold across all three — no <c>&lt;script&gt;</c>, no <c>&lt;style&gt;</c>, no <c>on*</c>
/// handler attributes, a URL scheme allowlist, forced <c>rel="noopener noreferrer"</c>, and a CSS
/// property allowlist — are not a profile's business to relax.
/// </remarks>
public enum SanitizationProfile
{
    /// <summary>
    /// Prose and links only. The default for <c>richText</c>.
    /// </summary>
    Basic = 0,

    /// <summary>
    /// Basic plus tables, images, figures, and layout containers with a class allowlist. Selected by
    /// a <c>richText</c> property configured for it.
    /// </summary>
    Extended = 1,

    /// <summary>
    /// Extended plus embeds — <c>iframe</c> against a host allowlist, media elements, and data
    /// attributes. Reachable only from the <c>html</c> field type, which is itself restricted to the
    /// <c>Developer</c> role.
    /// </summary>
    Developer = 2,
}
