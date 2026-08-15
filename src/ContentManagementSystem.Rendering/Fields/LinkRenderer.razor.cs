using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Core.Routing;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>link</c> value, whichever of the five kinds it is (spec section 7.1).
/// </summary>
/// <remarks>
/// <strong>This is the other half of decision D6.</strong> An internal link stores a page id and
/// never a URL, which is only worth anything because something resolves it late — here, at render
/// time, from the target's current route. That is what makes a link authored two years ago point at
/// the right place after its target has moved twice, with nothing having gone back to rewrite the
/// payload (acceptance criterion P3 #7).
/// <para>
/// Resolving also declares the dependency: a rendered internal link adds the target's
/// <c>page:{id}</c> cache tag, so moving or renaming that page evicts every page linking to it. A
/// link that resolved without tagging would leave the old URL cached on other pages.
/// </para>
/// <para>
/// An unresolvable page — deleted, or unpublished and this is the public site — renders the link's
/// text as plain text and logs. Emitting a dead <c>href</c> instead would put a 404 in front of a
/// reader, and rendering nothing would silently remove a sentence's own words.
/// </para>
/// <para>
/// Inside preview an unpublished target resolves to its draft URL and is badged
/// <c>cms-link-draft</c> (spec section 12.3), which is the whole point of preview being able to walk
/// an unreleased section. The public path never asks for one, so a draft URL cannot leak into a
/// cached page.
/// </para>
/// </remarks>
public partial class LinkRenderer : CmsFieldRendererBase
{
    private const string PageIdMember = "pageId";
    private const string MediaIdMember = "mediaId";
    private const string UrlMember = "url";
    private const string AnchorMember = "anchor";
    private const string EmailMember = "email";
    private const string TextMember = "text";
    private const string TargetMember = "target";
    private const string RelMember = "rel";

    /// <summary>The browsing contexts a link may name; anything else was a typo and is dropped.</summary>
    private static readonly string[] Targets = ["_self", "_blank", "_parent", "_top"];

    [Inject]
    private ILinkResolver Links { get; set; } = default!;

    [Inject]
    private ILogger<LinkRenderer> Logger { get; set; } = default!;

    /// <summary>Where the link points, or null when it resolved to nowhere.</summary>
    protected string? Href { get; private set; }

    /// <summary>The visible label; empty when there is nothing to show at all.</summary>
    protected string Text { get; private set; } = string.Empty;

    /// <summary>The stored browsing context, or null when none was chosen.</summary>
    protected string? Target { get; private set; }

    /// <summary>The link relationship, forced to carry <c>noopener noreferrer</c> where it matters.</summary>
    protected string? Rel { get; private set; }

    /// <summary>Whether the target is unpublished, which only preview can see.</summary>
    protected bool IsDraft { get; private set; }

    /// <summary>The anchor's classes, badging a draft target so preview says what it is showing.</summary>
    protected string CssClass => IsDraft ? "cms-link cms-link-draft" : "cms-link";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        Href = null;
        Text = StringMember(TextMember) ?? string.Empty;
        Target = null;
        Rel = null;
        IsDraft = false;

        var kind = StringMember(LinkFieldType.KindMember);

        switch (kind)
        {
            case LinkFieldType.PageKind:
                await ResolvePageAsync();
                break;

            case LinkFieldType.ExternalKind:
                ResolveExternal();
                break;

            case LinkFieldType.MediaKind:
                ResolveMedia();
                break;

            case LinkFieldType.AnchorKind:
                ResolveAnchor();
                break;

            case LinkFieldType.EmailKind:
                ResolveEmail();
                break;

            default:
                // No kind means nothing can say which member holds the destination. The text is
                // still the author's, so it is kept; only the anchor is lost.
                Logger.LogWarning(
                    "Link in '{PropertyKey}' on page {PageId} version {VersionId} declares no " +
                    "readable kind ('{Kind}'); its text renders without a destination.",
                    PropertyKey,
                    Context?.Page.Id,
                    Context?.Page.VersionId,
                    kind);
                break;
        }

        ApplyTargetAndRel(kind);
    }

    private async Task ResolvePageAsync()
    {
        if (IdMember(PageIdMember) is not { } pageId) return;

        // Tagged whether or not it resolves. A link to a page that does not exist yet still has to
        // be re-rendered when that page appears, and a tag only added on success would never fire.
        Context?.CacheTags.AddPage(pageId);

        var includeUnpublished = Context?.IsPreview ?? false;
        var resolved = await Links.ResolveAsync([pageId], includeUnpublished, CancellationToken.None);

        if (!resolved.TryGetValue(pageId, out var target) || target.Url is not { Length: > 0 } url)
        {
            Logger.LogWarning(
                "Link in '{PropertyKey}' on page {PageId} version {VersionId} points at page " +
                "{TargetId}, which has no URL this audience may see; it renders as plain text.",
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId,
                pageId);

            return;
        }

        Href = url;
        IsDraft = !target.IsPublished;

        // The target's own title, so a link whose author left the text empty still says something —
        // and says the current title rather than the one it had when the link was made.
        if (Text.Length == 0 && target.Title is { Length: > 0 } title)
        {
            Text = title;
        }
    }

    /// <summary>
    /// Applies the scheme allowlist to a stored external URL, on the way out.
    /// </summary>
    /// <remarks>
    /// The field type checks this on write too. Checking again here is not redundancy for its own
    /// sake: rows written before the check existed, imported, or restored from a backup never passed
    /// it, and <c>javascript:</c> in an <c>href</c> is a stored-XSS payload that the write-time check
    /// alone does not cover (ADR 0008).
    /// </remarks>
    private void ResolveExternal()
    {
        if (StringMember(UrlMember) is not { Length: > 0 } url) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            Logger.LogWarning(
                "Link in '{PropertyKey}' on page {PageId} version {VersionId} stores an external " +
                "URL that is not http or https; it renders as plain text.",
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId);

            return;
        }

        Href = url;

        if (Text.Length == 0)
        {
            Text = url;
        }
    }

    /// <summary>
    /// Declares the dependency on the linked file and leaves the URL to P5.
    /// </summary>
    /// <remarks>
    /// Same reasoning as <see cref="MediaRenderer"/>: the media library and its URL scheme arrive in
    /// P5, but the <c>media:{id}</c> tag has to be on every page that ever rendered the link, or the
    /// pages published before P5 are invisible to invalidation forever.
    /// </remarks>
    private void ResolveMedia()
    {
        if (IdMember(MediaIdMember) is not { } mediaId) return;

        Context?.CacheTags.AddMedia(mediaId);
    }

    private void ResolveAnchor()
    {
        if (StringMember(AnchorMember) is not { Length: > 0 } anchor) return;

        var fragment = anchor.TrimStart('#');

        if (fragment.Length == 0) return;

        Href = $"#{fragment}";

        if (Text.Length == 0)
        {
            Text = fragment;
        }
    }

    /// <summary>
    /// Builds a <c>mailto:</c>, re-checking the address for the reason <see cref="ResolveExternal"/>
    /// re-checks a scheme.
    /// </summary>
    /// <remarks>
    /// The check is the weakest one that still catches a mistake, matching the field type: an
    /// address is only really validated by delivering to it, and the strict patterns in circulation
    /// are all known for refusing addresses that work. What it does rule out is whitespace, which is
    /// how a second URL gets smuggled into an <c>href</c>.
    /// </remarks>
    private void ResolveEmail()
    {
        if (StringMember(EmailMember) is not { Length: > 0 } email) return;

        var at = email.IndexOf('@', StringComparison.Ordinal);

        if (email.AsSpan().ContainsAny(" \t\r\n") || at <= 0 || at >= email.Length - 1) return;

        Href = $"mailto:{email}";

        if (Text.Length == 0)
        {
            Text = email;
        }
    }

    /// <summary>
    /// Copies through the stored browsing context and forces the relationship that has to be there.
    /// </summary>
    /// <param name="kind">The link kind, which decides whether <c>noopener</c> is required.</param>
    /// <remarks>
    /// <c>noopener noreferrer</c> is <em>merged</em> into whatever the author stored rather than
    /// replacing it, so an editor's <c>nofollow</c> survives — the same rule the sanitizer applies to
    /// anchors inside rich text (spec section 20.2), applied here because an anchor this class
    /// builds never passes through the sanitizer.
    /// <para>
    /// Forced for every external link, not only for <c>_blank</c> ones. A target can be changed to
    /// <c>_blank</c> by a stylesheet-driven script or by a browser's own middle-click, and
    /// <c>noreferrer</c> is worth having on an outbound link regardless.
    /// </para>
    /// </remarks>
    private void ApplyTargetAndRel(string? kind)
    {
        if (Href is null) return;

        var target = StringMember(TargetMember);

        Target = target is not null && Array.IndexOf(Targets, target) >= 0 ? target : null;

        var relations = new List<string>();

        if (StringMember(RelMember) is { Length: > 0 } stored)
        {
            relations.AddRange(stored.Split(' ', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
        }

        if (Target == "_blank" || kind == LinkFieldType.ExternalKind)
        {
            foreach (var required in (string[])["noopener", "noreferrer"])
            {
                if (!relations.Contains(required, StringComparer.OrdinalIgnoreCase))
                {
                    relations.Add(required);
                }
            }
        }

        Rel = relations.Count > 0 ? string.Join(' ', relations) : null;
    }
}
