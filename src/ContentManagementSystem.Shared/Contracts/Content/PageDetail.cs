namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// A page as a list or a tree node shows it.
/// </summary>
/// <param name="Id">Database identity, used to address the page in the API.</param>
/// <param name="PublicId">Stable external identifier, safe to expose outside the database.</param>
/// <param name="ParentId">Parent node, or null for a page at the root of the site.</param>
/// <param name="Title">Title as at the draft version.</param>
/// <param name="Slug">The page's own URL segment.</param>
/// <param name="Depth">Number of ancestors above the page.</param>
/// <param name="SortOrder">Order among siblings.</param>
/// <param name="TemplateId">Template that lays out the page.</param>
/// <param name="TemplateKey">Key of that template, so a client need not resolve the id.</param>
/// <param name="Status">
/// Status of the draft version. Whether the page is <em>live</em> is
/// <paramref name="PublishedVersionNumber"/>, not this — a page with a published version and a
/// draft being edited is both at once.
/// </param>
/// <param name="HasUnpublishedChanges">
/// Whether the draft has moved on from what is published. False for a page that has never been
/// published, which is reported by a null <paramref name="PublishedVersionNumber"/> instead.
/// </param>
/// <param name="DraftVersionNumber">Version number of the working draft.</param>
/// <param name="PublishedVersionNumber">Version number being served publicly, or null.</param>
/// <param name="ShowInNavigation">Whether generated navigation menus include the page.</param>
/// <param name="HasChildren">Whether the page has any live children, for the tree's expander.</param>
/// <param name="ModifiedOn">When the page row itself last changed.</param>
/// <param name="ScheduledPublishOn">
/// When the draft is due to go live, or null when it publishes on request. Carried here rather than
/// folded into <paramref name="Status"/> because a scheduled page is a draft — the schedule is an
/// additional fact about it, not a different lifecycle state (spec section 11.6).
/// </param>
/// <param name="LockedBy">
/// Display name of whoever currently has the page open in the editor, or null when nobody does. A
/// lock never prevents editing (ADR 0012); this is here so the tree can say who is in there before
/// somebody opens the same page.
/// </param>
public sealed record PageSummary(
    int Id,
    Guid PublicId,
    int? ParentId,
    string Title,
    string Slug,
    int Depth,
    int SortOrder,
    int TemplateId,
    string TemplateKey,
    string Status,
    bool HasUnpublishedChanges,
    int DraftVersionNumber,
    int? PublishedVersionNumber,
    bool ShowInNavigation,
    bool HasChildren,
    DateTimeOffset? ModifiedOn,
    DateTimeOffset? ScheduledPublishOn = null,
    string? LockedBy = null);

/// <summary>
/// A page's metadata and its draft payload, as <c>GET /api/cms/v1/pages/{id}</c> returns them.
/// </summary>
/// <param name="Summary">What a list or tree node shows.</param>
/// <param name="ContentJson">
/// The draft version's payload, verbatim (spec section 6.2).
/// </param>
/// <param name="TemplateRevision">
/// Template revision the draft payload was authored against. The pair of this and
/// <c>Summary.TemplateKey</c> is what the editor resolves zone definitions by — never the template's
/// current revision, which may have moved on (spec section 8.5).
/// </param>
/// <param name="UseExplicitUrl">Whether the page's URL is stated outright rather than built from the tree.</param>
/// <param name="ExplicitUrl">Full site-relative URL when <paramref name="UseExplicitUrl"/> is set.</param>
/// <param name="OwnerUserId">Editor accountable for keeping the page current.</param>
/// <param name="ReviewByDate">Date the content is due a review.</param>
/// <param name="InternalNotes">Note to other editors. Never rendered publicly.</param>
/// <param name="Seo">Search and social metadata carried by the draft version.</param>
/// <param name="RowVersion">
/// The draft version's concurrency token, Base64-encoded. The value a client echoes back as
/// <c>If-Match</c> on a draft save (spec section 11.8).
/// </param>
/// <remarks>
/// The payload is handed over as stored rather than deserialized into a model, for the reason
/// <c>ContentPayload</c> gives: zones are user-defined data with no CLR type to bind to, and binding
/// to one anyway loses the absent-versus-null distinction the editor depends on.
/// </remarks>
public sealed record PageDetail(
    PageSummary Summary,
    string ContentJson,
    int TemplateRevision,
    bool UseExplicitUrl,
    string? ExplicitUrl,
    int? OwnerUserId,
    DateOnly? ReviewByDate,
    string? InternalNotes,
    PageSeo Seo,
    string RowVersion);

/// <summary>
/// The search and social metadata one page version carries (spec section 18.1).
/// </summary>
/// <param name="MetaTitle">Overrides the <c>&lt;title&gt;</c> element.</param>
/// <param name="MetaDescription">Meta description rendered into the page head.</param>
/// <param name="CanonicalUrl">Explicit canonical URL.</param>
/// <param name="RobotsIndex">Whether search engines may index the page.</param>
/// <param name="RobotsFollow">Whether search engines may follow links out of the page.</param>
/// <param name="OgTitle">Open Graph title.</param>
/// <param name="OgDescription">Open Graph description.</param>
/// <param name="OgImageMediaId">Media item used as the Open Graph image.</param>
/// <param name="OgType">Open Graph object type.</param>
/// <param name="TwitterCard">Twitter card type.</param>
/// <param name="StructuredDataJson">Hand-authored JSON-LD emitted into the page head.</param>
/// <param name="ChangeFreq">Sitemap change-frequency hint.</param>
/// <param name="Priority">Sitemap priority between 0.0 and 1.0.</param>
/// <remarks>
/// Grouped rather than spread across <see cref="PageDetail"/> because these thirteen values are
/// versioned together, are edited on one panel, and are copied wholesale by publishing and by
/// duplication — three places that would otherwise each list them.
/// </remarks>
public sealed record PageSeo(
    string? MetaTitle,
    string? MetaDescription,
    string? CanonicalUrl,
    bool RobotsIndex,
    bool RobotsFollow,
    string? OgTitle,
    string? OgDescription,
    int? OgImageMediaId,
    string? OgType,
    string? TwitterCard,
    string? StructuredDataJson,
    string? ChangeFreq,
    decimal? Priority);
