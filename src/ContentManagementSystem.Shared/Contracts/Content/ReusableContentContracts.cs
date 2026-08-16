using System.Text.Json.Serialization;

using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Body of <c>POST /api/cms/v1/reusable</c>.
/// </summary>
/// <param name="BlockTypeId">
/// Block type whose property set is the item's shape. Immutable once the item exists — the same rule
/// as a page's template, for the same reason (spec section 9.1).
/// </param>
/// <param name="Name">Editor-facing display name, shown in the library and the picker.</param>
/// <param name="Key">
/// Stable identifier. Omitted, it is generated from <paramref name="Name"/>; supplied, it is checked
/// against the same shape rules templates and block types obey.
/// </param>
/// <param name="Description">Optional help text describing when to reach for this item.</param>
/// <param name="FolderId">Organizational grouping inside the library.</param>
/// <remarks>
/// Deliberately small, like <see cref="CreatePageRequest"/>. Content is written through the draft
/// endpoint and status only through the lifecycle endpoints, so nothing here can put an item into a
/// state the dedicated route would have checked (spec section 20.1).
/// </remarks>
public sealed record CreateReusableContentRequest(
    int BlockTypeId,
    string? Name,
    string? Key = null,
    string? Description = null,
    int? FolderId = null);

/// <summary>
/// Body of <c>PATCH /api/cms/v1/reusable/{id}</c>.
/// </summary>
/// <remarks>
/// Every member is a <see cref="Patch{T}"/>, so omitting one leaves the stored value alone and
/// sending it as <c>null</c> clears it.
/// <para>
/// Neither the key nor the block type appears here, and that is the point of the request existing
/// separately from the create: both are quoted by stored content, and a rename content cannot follow
/// is how a library ends up with items nothing can resolve (spec section 8.5).
/// </para>
/// </remarks>
public sealed record PatchReusableContentRequest
{
    /// <summary>Editor-facing display name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string> Name { get; init; }

    /// <summary>Help text describing when to reach for this item.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> Description { get; init; }

    /// <summary>Organizational grouping inside the library, or null for the top level.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<int?> FolderId { get; init; }

    /// <summary>
    /// The item's <c>rowversion</c> as the caller last saw it, Base64-encoded. Null skips the check.
    /// </summary>
    public string? ExpectedRowVersion { get; init; }
}

/// <summary>
/// A reusable item as the library list shows it.
/// </summary>
/// <param name="Id">Database identity, used to address the item in the API and in placements.</param>
/// <param name="Key">Stable identifier.</param>
/// <param name="Name">Editor-facing display name.</param>
/// <param name="Description">Help text describing when to reach for this item.</param>
/// <param name="FolderId">Organizational grouping.</param>
/// <param name="BlockTypeId">Block type whose property set is the item's shape.</param>
/// <param name="BlockTypeKey">Key of that block type, so a client need not resolve the id.</param>
/// <param name="Status">Status of the draft version.</param>
/// <param name="HasUnpublishedChanges">Whether the draft has moved on from what is published.</param>
/// <param name="DraftVersionNumber">Version number of the working draft.</param>
/// <param name="PublishedVersionNumber">
/// Version number every late-bound placement currently renders, or null while the item has never
/// been published — in which case every page placing it renders nothing (spec section 15.3).
/// </param>
/// <param name="ModifiedOn">When the item row itself last changed.</param>
public sealed record ReusableContentSummary(
    int Id,
    string Key,
    string Name,
    string? Description,
    int? FolderId,
    int BlockTypeId,
    string BlockTypeKey,
    string Status,
    bool HasUnpublishedChanges,
    int DraftVersionNumber,
    int? PublishedVersionNumber,
    DateTimeOffset? ModifiedOn);

/// <summary>
/// A reusable item's metadata and its draft payload, as <c>GET /api/cms/v1/reusable/{id}</c> returns
/// them.
/// </summary>
/// <param name="Summary">What the library list shows.</param>
/// <param name="ContentJson">The draft version's payload, verbatim (spec section 6.2).</param>
/// <param name="BlockTypeRevision">
/// Block type revision the draft payload was authored against. The pair of this and
/// <c>Summary.BlockTypeKey</c> is what the editor resolves property definitions by — never the block
/// type's current revision, which may have moved on (spec section 8.5).
/// </param>
/// <param name="RowVersion">
/// The draft version's concurrency token, Base64-encoded, echoed back as <c>If-Match</c> on a save.
/// </param>
public sealed record ReusableContentDetail(
    ReusableContentSummary Summary,
    string ContentJson,
    int BlockTypeRevision,
    string RowVersion);

/// <summary>
/// The working draft of a reusable item.
/// </summary>
/// <param name="ReusableContentId">Item the draft belongs to.</param>
/// <param name="VersionId">Identity of the draft version row.</param>
/// <param name="VersionNumber">Its version number, which a draft keeps for its whole life.</param>
/// <param name="ContentJson">The payload as stored.</param>
/// <param name="BlockTypeKey">Key of the block type the payload is authored against.</param>
/// <param name="BlockTypeRevision">The revision the payload captured (spec section 8.5).</param>
/// <param name="RowVersion">The concurrency token to echo back on the next save, Base64-encoded.</param>
/// <param name="SavedOn">When the draft was last written.</param>
public sealed record ReusableDraftState(
    int ReusableContentId,
    int VersionId,
    int VersionNumber,
    string ContentJson,
    string BlockTypeKey,
    int BlockTypeRevision,
    string RowVersion,
    DateTimeOffset? SavedOn);

/// <summary>
/// The outcome of writing to a reusable item's draft.
/// </summary>
/// <param name="Draft">
/// The draft as it now stands. On a conflict this is the <em>stored</em> draft — the one that won.
/// </param>
/// <param name="Warnings">Non-blocking diagnostics from the payload walk.</param>
/// <param name="ReferenceCount">
/// How many <c>ContentReference</c> rows the payload projected to. A reusable item's own references
/// are what make a nested placement resolvable and a cycle detectable, so a save that silently
/// produced none is worth being able to see.
/// </param>
public sealed record ReusableDraftSaveResult(
    ReusableDraftState Draft,
    IReadOnlyList<ApiDiagnostic> Warnings,
    int ReferenceCount);

/// <summary>
/// One entry of a reusable item's version history.
/// </summary>
/// <param name="Id">Identity of the version, used to address it in the API and by a pin.</param>
/// <param name="VersionNumber">Its number within the item.</param>
/// <param name="Status">Where it sits in the editorial lifecycle.</param>
/// <param name="Label">Editor-supplied name, present only on a named checkpoint.</param>
/// <param name="BlockTypeRevision">Block type revision its payload was authored against.</param>
/// <param name="IsDraft">Whether this is the item's one mutable working version.</param>
/// <param name="IsPublished">Whether this is the version late-bound placements are rendering.</param>
/// <param name="CreatedOn">When the row was written.</param>
/// <param name="CreatedBy">Who wrote it.</param>
/// <param name="PublishedOn">When it went live, if it ever did.</param>
/// <param name="PublishedBy">Who published it.</param>
public sealed record ReusableVersionSummary(
    int Id,
    int VersionNumber,
    string Status,
    string? Label,
    int BlockTypeRevision,
    bool IsDraft,
    bool IsPublished,
    DateTimeOffset? CreatedOn,
    int CreatedBy,
    DateTimeOffset? PublishedOn,
    int? PublishedBy);

/// <summary>
/// What a dry-run publish check found for a reusable item.
/// </summary>
/// <param name="CanPublish">Whether a publish attempted now would be accepted.</param>
/// <param name="Errors">Everything blocking.</param>
/// <param name="Warnings">Everything worth showing but not blocking.</param>
/// <param name="Impact">
/// What the publish would change (spec section 9.4). Returned by the <em>check</em> and not only by
/// the publish, because the confirmation dialog has to be shown before the irreversible part.
/// </param>
public sealed record ReusablePublishValidation(
    bool CanPublish,
    IReadOnlyList<ApiDiagnostic> Errors,
    IReadOnlyList<ApiDiagnostic> Warnings,
    ReferenceImpact Impact);

/// <summary>
/// What publishing a reusable item did.
/// </summary>
/// <param name="ReusableContentId">Item published.</param>
/// <param name="VersionId">Identity of the new immutable version.</param>
/// <param name="VersionNumber">Its version number.</param>
/// <param name="PublishedOn">When it went live.</param>
/// <param name="ArchivedVersionNumber">The version it superseded, or null on a first publish.</param>
/// <param name="ReferenceCount">How many <c>ContentReference</c> rows the published payload projected to.</param>
/// <param name="Impact">
/// The pages this publish changed, recorded as at the moment of the publish. This is the list spec
/// section 9.3 requires the audit entry to carry, so that "why did 40 pages change at 14:02?" has an
/// answer months later, when the references have moved on.
/// </param>
/// <param name="Warnings">Non-blocking diagnostics the publish went ahead despite.</param>
public sealed record ReusablePublishResult(
    int ReusableContentId,
    int VersionId,
    int VersionNumber,
    DateTimeOffset PublishedOn,
    int? ArchivedVersionNumber,
    int ReferenceCount,
    ReferenceImpact Impact,
    IReadOnlyList<ApiDiagnostic> Warnings);

/// <summary>
/// What unpublishing a reusable item retired.
/// </summary>
/// <param name="ReusableContentId">Item retired.</param>
/// <param name="UnpublishedVersionNumber">The version that stopped being served.</param>
/// <param name="Impact">
/// The pages that will now render nothing where the item was (spec section 15.3). Reported because
/// unpublishing shared content is the one lifecycle action whose damage is entirely off-screen.
/// </param>
public sealed record ReusableUnpublishResult(
    int ReusableContentId,
    int UnpublishedVersionNumber,
    ReferenceImpact Impact);

/// <summary>
/// What a delete did, or would have done.
/// </summary>
/// <param name="ReusableContentId">Item deleted.</param>
/// <param name="WasPublished">Whether the item was live when it was deleted.</param>
public sealed record ReusableDeleteResult(int ReusableContentId, bool WasPublished);
