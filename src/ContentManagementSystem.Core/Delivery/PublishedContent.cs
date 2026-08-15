using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Shared.Content;

namespace ContentManagementSystem.Core.Delivery;

/// <summary>
/// One page version, loaded and deserialized, ready to be rendered (task P3-12).
/// </summary>
/// <remarks>
/// Everything the delivery path needs and nothing more. It carries no entities, no navigation
/// properties, and no database context, which is what makes it safe to hold: a renderer must not be
/// able to walk from the page it is rendering to the rest of the content tree, and a cached object
/// holding a tracked entity is a lazy load waiting to happen on a disposed context.
/// <para>
/// <strong>Immutable, and deliberately cacheable.</strong> Spec section 16.1 gives the published
/// content cache a fifteen-minute TTL over exactly this object. Every member here is either a value
/// or an already-immutable reference — <see cref="ContentPayload"/> holds a detached
/// <c>JsonElement</c> clone and owns no <c>IDisposable</c>, and a <see cref="ContentSchema"/> is
/// built once per revision and shared. Nothing here accumulates per-request state; the thing that
/// does, the cache tag set, lives on the render context and is created per render for that reason
/// (S2 spike, "what this spike did not cover").
/// </para>
/// <para>
/// A flat record rather than a page object plus a content object, because the split would be
/// arbitrary: a version <em>is</em> its metadata and its payload together, and the two are read in
/// one query and cached as one entry.
/// </para>
/// </remarks>
/// <param name="PageId">Page id, used in render diagnostics and in the <c>page:{id}</c> cache tag.</param>
/// <param name="PublicId">Stable external identifier, safe to put in markup.</param>
/// <param name="VersionId">Id of the version loaded.</param>
/// <param name="VersionNumber">That version's number within the page.</param>
/// <param name="Title">Page title as at this version.</param>
/// <param name="Url">Site-relative URL the version is reachable at.</param>
/// <param name="TemplateId">Template id, for the <c>tpl:{id}</c> cache tag.</param>
/// <param name="TemplateKey">Template key, which selects the component that renders the page.</param>
/// <param name="TemplateRevision">Revision whose captured schema this version was authored against.</param>
/// <param name="IsPublished">Whether this exact version is the one anonymous visitors see.</param>
/// <param name="PublishedOn">When the version was published, or null while it never has been.</param>
/// <param name="ModifiedOn">When the version last changed, for <c>Last-Modified</c>.</param>
/// <param name="MetaTitle">Overrides the document title; null falls back to <paramref name="Title"/>.</param>
/// <param name="MetaDescription">Meta description for the document head.</param>
/// <param name="CanonicalUrl">Explicit canonical URL; null falls back to <paramref name="Url"/>.</param>
/// <param name="RobotsIndex">Whether search engines may index the page.</param>
/// <param name="RobotsFollow">Whether search engines may follow links out of the page.</param>
/// <param name="Payload">The version's content.</param>
/// <param name="Schema">
/// The captured schema of the template revision the payload names, or null when the revision could
/// not be resolved. Null is a rendering condition, not an error: the payload's own type
/// discriminators are enough to read every value, and only the field configuration is lost
/// (spec section 8.5).
/// </param>
public sealed record PublishedContent(
    int PageId,
    Guid PublicId,
    int VersionId,
    int VersionNumber,
    string Title,
    string Url,
    int TemplateId,
    string TemplateKey,
    int TemplateRevision,
    bool IsPublished,
    DateTimeOffset? PublishedOn,
    DateTimeOffset? ModifiedOn,
    string? MetaTitle,
    string? MetaDescription,
    string? CanonicalUrl,
    bool RobotsIndex,
    bool RobotsFollow,
    ContentPayload Payload,
    ContentSchema? Schema)
{
    /// <summary>The title the document's <c>&lt;title&gt;</c> element carries.</summary>
    /// <remarks>
    /// Falls back rather than defaulting at write time, so that renaming a page changes its document
    /// title unless an editor has deliberately overridden it (spec section 18.1).
    /// </remarks>
    public string DocumentTitle => string.IsNullOrWhiteSpace(MetaTitle) ? Title : MetaTitle;

    /// <summary>The URL the canonical link element points at.</summary>
    public string CanonicalOrOwnUrl =>
        string.IsNullOrWhiteSpace(CanonicalUrl) ? Url : CanonicalUrl;
}
