namespace ContentManagementSystem.Client.Components.Admin.Pickers;

/// <summary>
/// The kinds of destination a link can name, and the member each one stores it under
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// These mirror the constants on <c>LinkFieldType</c>, which the backoffice cannot reference —
/// <c>Core</c> is not loaded in the browser. They are part of the stored payload contract either
/// way, so the duplication is of a string that could not have been changed on one side alone.
/// </remarks>
public static class LinkKinds
{
    /// <summary>The member deciding which destination member applies.</summary>
    public const string KindMember = "kind";

    /// <summary>A link to another page in this site, stored by identity.</summary>
    public const string Page = "page";

    /// <summary>A link to a URL on another site.</summary>
    public const string External = "external";

    /// <summary>A link to a file in the media library, stored by identity.</summary>
    public const string Media = "media";

    /// <summary>A jump to a fragment on the page the link is rendered on.</summary>
    public const string Anchor = "anchor";

    /// <summary>A <c>mailto:</c> link.</summary>
    public const string Email = "email";

    /// <summary>The page a <see cref="Page"/> link points at.</summary>
    public const string PageIdMember = "pageId";

    /// <summary>The item a <see cref="Media"/> link points at.</summary>
    public const string MediaIdMember = "mediaId";

    /// <summary>The address an <see cref="External"/> link points at.</summary>
    public const string UrlMember = "url";

    /// <summary>The fragment an <see cref="Anchor"/> link jumps to.</summary>
    public const string AnchorMember = "anchor";

    /// <summary>The address an <see cref="Email"/> link opens.</summary>
    public const string EmailMember = "email";

    /// <summary>What the link reads as, when the template shows the author's own words.</summary>
    public const string TextMember = "text";

    /// <summary>The browsing context the link opens in.</summary>
    public const string TargetMember = "target";

    /// <summary>Every kind, in the order a picker offers them.</summary>
    /// <remarks>
    /// Internal destinations first. A CMS-aware link picker exists to make the internal choice the
    /// easy one — a link stored as a page id survives the target being moved or renamed, and a
    /// hand-typed URL to the same page does not (ADR-0006).
    /// </remarks>
    public static IReadOnlyList<string> All { get; } = [Page, Media, External, Email, Anchor];

    /// <summary>What to call a kind in front of an author.</summary>
    /// <param name="kind">The stored kind.</param>
    /// <returns>The label.</returns>
    public static string Label(string kind) => kind switch
    {
        Page => "A page on this site",
        Media => "A file in the library",
        External => "Another website",
        Email => "An email address",
        Anchor => "Somewhere on this page",
        _ => kind,
    };

    /// <summary>The browsing contexts a link may name.</summary>
    public static IReadOnlyList<string> Targets { get; } = ["_self", "_blank"];
}
