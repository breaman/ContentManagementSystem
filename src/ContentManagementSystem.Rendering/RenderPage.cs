namespace ContentManagementSystem.Rendering;

/// <summary>
/// The page facts a template, block, or field renderer may read: one page version, resolved and
/// ready to render (spec section 15.2).
/// </summary>
/// <remarks>
/// Spec section 15.2 names this member's type <c>PublishedPage</c>. It is <c>RenderPage</c> here for
/// one reason: preview renders <em>any</em> version through this same pipeline (spec section 12.1),
/// so a type asserting in its name that its contents are published would be a lie on every preview
/// request — and the kind of lie someone eventually leans on, by skipping a published check because
/// the type already promised one. Whether a version is live is the explicit
/// <see cref="IsPublished"/> flag, and the guarantee that anonymous delivery never loads a draft is
/// enforced where it belongs, in the query (spec section 20.1).
/// <para>
/// It carries no navigation properties and nothing lazily loaded. A renderer runs with no database
/// context and must not be able to walk from the page it is rendering to the rest of the content
/// tree — the resolution a renderer legitimately needs (a link's current URL, a media rendition) is
/// a batched service call, not a graph walk.
/// </para>
/// </remarks>
/// <param name="Id">Page id, used in every render diagnostic and in the <c>page:{id}</c> cache tag.</param>
/// <param name="PublicId">Stable external identifier, safe to put in markup.</param>
/// <param name="VersionId">Id of the version being rendered.</param>
/// <param name="VersionNumber">That version's number within the page.</param>
/// <param name="Title">Page title as at this version.</param>
/// <param name="Url">Site-relative URL this version is reachable at.</param>
/// <param name="TemplateId">Template id, for the <c>tpl:{id}</c> cache tag.</param>
/// <param name="TemplateKey">Template key, which selects the component that renders the page.</param>
/// <param name="TemplateRevision">Revision whose captured schema this version was authored against.</param>
/// <param name="IsPublished">Whether this exact version is the one anonymous visitors see.</param>
/// <param name="PublishedOn">When the version was published, or null while it never has been.</param>
/// <param name="ModifiedOn">When the version last changed, for <c>Last-Modified</c>.</param>
public sealed record RenderPage(
    int Id,
    Guid PublicId,
    int VersionId,
    int VersionNumber,
    string Title,
    string Url,
    int TemplateId,
    string TemplateKey,
    int TemplateRevision,
    bool IsPublished,
    DateTimeOffset? PublishedOn = null,
    DateTimeOffset? ModifiedOn = null);
