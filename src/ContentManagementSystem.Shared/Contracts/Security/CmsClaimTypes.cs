namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// Claim types the CMS adds to the signed-in principal.
/// </summary>
public static class CmsClaimTypes
{
    /// <summary>
    /// One claim per <see cref="CmsPermissions"/> constant the caller's roles grant.
    /// </summary>
    /// <remarks>
    /// <strong>A display convenience, never the check.</strong> The backoffice runs in WebAssembly,
    /// where the server's authorization policies do not exist, so without this every screen either
    /// restates the role-to-permission table or shows an editor a button that is refused when they
    /// press it. Stamping the answer onto the principal keeps the table in one place.
    /// <para>
    /// It is not authoritative because it cannot be: claims are baked into the cookie at sign-in, so
    /// a permission removed from a role this morning is still on a principal issued last night. Every
    /// decision that matters is made server-side, per request, by <see cref="ICmsAuthorization"/>
    /// and — for anything scoped to a page — <see cref="IAclService"/>. A tampered or stale claim
    /// therefore buys nothing but a button that produces a <c>403</c>.
    /// </para>
    /// </remarks>
    public const string Permission = "cms:permission";

    /// <summary>
    /// Whether the account has a second factor set up, as <c>bool.TrueString</c> or absent.
    /// </summary>
    /// <remarks>
    /// Stamped so that the mandatory-2FA rule of spec section 20.3 costs a string comparison per
    /// request rather than a query (task P9-04). Like <see cref="Permission"/> it is a copy of
    /// something the database owns, and the direction it can be stale in is the safe one: the claim
    /// says "not enrolled" until the sign-in is refreshed, so a lapse gates an editor who has already
    /// enrolled rather than admitting one who has not.
    /// </remarks>
    public const string TwoFactorEnabled = "cms:2fa";
}
