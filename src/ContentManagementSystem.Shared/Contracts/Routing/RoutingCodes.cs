namespace ContentManagementSystem.Shared.Contracts.Routing;

/// <summary>
/// Stable diagnostic codes returned by the routing and redirect services.
/// </summary>
/// <remarks>
/// Separate from <c>PageCodes</c> for the reason that list gives for its own existence: a code is
/// what a client switches on to offer a remedy, and the remedy for "another page already serves this
/// URL" is nothing like the remedy for "this slug is not a usable segment". A code does not change
/// once shipped; the wording beside it may be rewritten freely (spec section 22.2).
/// </remarks>
public static class RoutingCodes
{
    /// <summary>The redirect addressed does not exist.</summary>
    public const string NotFound = "redirect.not-found";

    /// <summary>The caller is authenticated but holds no role permitting this.</summary>
    public const string Forbidden = "redirect.forbidden";

    /// <summary>
    /// A published page already serves this URL.
    /// </summary>
    /// <remarks>
    /// Reported rather than left to the filtered unique index, because a constraint violation
    /// reaches the client as a 500 and names no page. This names the page holding the URL, which is
    /// the only thing that lets an editor act on it.
    /// </remarks>
    public const string UrlTaken = "route.url-taken";

    /// <summary>The computed URL is longer than the column that stores it.</summary>
    /// <remarks>
    /// Reachable without anybody typing a long slug: a URL is its ancestors' slugs joined, so a
    /// move can push a whole subtree over the limit in one operation.
    /// </remarks>
    public const string UrlTooLong = "route.url-too-long";

    /// <summary>The redirect source is not a usable site-relative URL.</summary>
    public const string SourceInvalid = "redirect.source-invalid";

    /// <summary>The redirect has no destination, or names two.</summary>
    public const string DestinationInvalid = "redirect.destination-invalid";

    /// <summary>The destination page does not exist, or is in the recycle bin.</summary>
    public const string DestinationNotFound = "redirect.destination-not-found";

    /// <summary>
    /// The redirect would send a URL to itself, directly or around a chain.
    /// </summary>
    /// <remarks>
    /// Refused at write time rather than detected at resolve time — though it is also detected
    /// there, because a chain assembled by several separate writes can still close (spec section
    /// 10.5).
    /// </remarks>
    public const string Loop = "redirect.loop";

    /// <summary>Another redirect already claims this source URL.</summary>
    public const string SourceTaken = "redirect.source-taken";

    /// <summary>The status code is neither 301 nor 302.</summary>
    public const string StatusInvalid = "redirect.status-invalid";

    /// <summary>
    /// A row in an imported CSV could not be read, and was skipped.
    /// </summary>
    /// <remarks>
    /// A warning per row rather than a failed import: a legacy redirect list is thousands of rows
    /// long and typically has a handful of bad ones, and refusing the whole file leaves the operator
    /// editing a spreadsheet by hand with no report of what was wrong.
    /// </remarks>
    public const string ImportRowInvalid = "redirect.import-row-invalid";
}
