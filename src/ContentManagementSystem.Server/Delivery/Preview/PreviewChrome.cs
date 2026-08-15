using ContentManagementSystem.Core.Preview;

namespace ContentManagementSystem.Server.Delivery.Preview;

/// <summary>
/// Everything the preview furniture needs: what is on screen, and where the links go
/// (tasks P3-16 and P3-21, spec sections 12.1 and 12.3).
/// </summary>
/// <remarks>
/// <strong>The chrome and the page are two documents, not one.</strong> The toolbar and the
/// device-width frame live in this outer document; the page itself is rendered into an
/// <c>iframe</c> by exactly the same code, and through exactly the same components, that serve it to
/// an anonymous visitor. That is what makes preview fidelity structural rather than aspirational
/// (spec section 12.1): there is no branch in the delivery document for "but this is a preview", so
/// there is nothing that can drift.
/// <para>
/// It also gives spec section 12.3's device widths for free — constraining an <c>iframe</c> actually
/// constrains the viewport a media query reads, whereas a <c>div</c> with a maximum width does not,
/// and a preview that lies about breakpoints is worse than one that has none.
/// </para>
/// </remarks>
/// <param name="BasePath">
/// The preview URL without a query, either <c>/preview/{pageId}</c> or <c>/preview/s/{token}</c>.
/// Every link the toolbar renders is built from it, so the two entry points need no separate
/// link-building code and cannot disagree about the shape of the path.
/// </param>
/// <param name="Version">What version is on screen.</param>
/// <param name="Device">The width the frame is constrained to.</param>
/// <param name="VersionId">
/// The version pinned in the query string, or null to follow the page's draft. A shared link always
/// pins one; an editor's preview only does when they asked for a specific version.
/// </param>
/// <param name="ExitUrl">
/// Where the exit link goes, or null when there is nowhere to exit to. Null is the anonymous case:
/// the holder of a shared link has no backoffice to be returned to, and a link to one would be an
/// invitation to a login screen they cannot pass.
/// </param>
/// <param name="ExpiresOn">
/// When the shared link stops working, or null for an editor's own preview. Shown because the
/// commonest thing to go wrong with a review link is that it quietly ran out.
/// </param>
public sealed record PreviewChrome(
    string BasePath,
    PreviewVersionInfo Version,
    PreviewDevice Device,
    int? VersionId = null,
    string? ExitUrl = null,
    DateTimeOffset? ExpiresOn = null)
{
    /// <summary>Path segment the framed page is served at, appended to the base path.</summary>
    public const string ContentSegment = "/content";

    /// <summary>The <c>src</c> of the frame holding the page.</summary>
    /// <remarks>
    /// The device is deliberately not passed down. The page inside the frame must be byte-identical
    /// to what the public site serves, and a page that could read which device frame it was in would
    /// be a page that could render differently inside preview.
    /// </remarks>
    public string ContentUrl => BasePath + ContentSegment + VersionQuery;

    /// <summary>The CSS class constraining the frame to the chosen width.</summary>
    public string ViewportCssClass => PreviewDevices.CssClass(Device);

    /// <summary>Whether this is a shared link rather than an editor's own preview.</summary>
    public bool IsShared => ExitUrl is null;

    /// <summary>The URL that shows the same version at another width.</summary>
    /// <param name="device">The width to switch to.</param>
    /// <returns>The link for the toolbar's device button.</returns>
    public string UrlFor(PreviewDevice device)
    {
        var version = VersionQuery;

        return device is PreviewDevice.Desktop && version.Length == 0
            ? BasePath
            : $"{BasePath}{(version.Length == 0 ? "?" : version + "&")}device={PreviewDevices.Key(device)}";
    }

    /// <summary>The version query string, including its leading <c>?</c>, or empty.</summary>
    private string VersionQuery => VersionId is { } id ? $"?version={id}" : string.Empty;
}
