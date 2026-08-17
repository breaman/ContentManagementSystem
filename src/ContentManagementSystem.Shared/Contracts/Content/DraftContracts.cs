using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Body of <c>PUT /api/cms/v1/pages/{id}/draft</c>.
/// </summary>
/// <param name="ContentJson">
/// The whole payload, as the editor now holds it (spec section 6.2). Whole rather than a patch: the
/// document is the unit the schema validates and the reference indexer walks, and a partial write
/// would have to be reassembled into one anyway before either could run.
/// </param>
/// <param name="ExpectedRowVersion">
/// The draft's <c>rowversion</c> as the editor last saw it, Base64-encoded — the value
/// <c>PageDetail.RowVersion</c> handed over, and what an <c>If-Match</c> header carries. Null skips
/// the check, which only an operation with no earlier read has any business doing.
/// </param>
public sealed record SaveDraftRequest(string? ContentJson, string? ExpectedRowVersion);

/// <summary>
/// Body of <c>POST /api/cms/v1/pages/{id}/draft/diff</c> — what the caller holds, to be compared
/// against what is stored (task P6-19).
/// </summary>
/// <param name="ContentJson">
/// The payload as the caller holds it, unsaved. The comparison reads the stored draft as the earlier
/// side and this as the later one, so it answers "what would my copy change" rather than the other
/// way round.
/// </param>
/// <remarks>
/// The version diff cannot answer this. Both copies of a contested draft are the <em>same</em>
/// version row — the losing editor's is in a browser and was never written — so there is no second
/// version id to name, and the only way to compare them structurally is to send one of them.
/// </remarks>
public sealed record DiffDraftRequest(string? ContentJson);

/// <summary>
/// Body of <c>POST /api/cms/v1/pages/{id}/draft/checkpoint</c>.
/// </summary>
/// <param name="Label">
/// What the editor is marking, such as "before the big rewrite". Optional, because the useful thing
/// about a checkpoint is that it exists; a nameless one still shows in the history with its
/// timestamp.
/// </param>
public sealed record CheckpointDraftRequest(string? Label = null);

/// <summary>
/// The working draft of a page.
/// </summary>
/// <param name="PageId">Page the draft belongs to.</param>
/// <param name="VersionId">Identity of the draft version row.</param>
/// <param name="VersionNumber">Its version number, which a draft keeps for its whole life.</param>
/// <param name="ContentJson">The payload as stored.</param>
/// <param name="TemplateKey">Key of the template the payload is authored against.</param>
/// <param name="TemplateRevision">The revision the payload captured (spec section 8.5).</param>
/// <param name="RowVersion">The concurrency token to echo back on the next save, Base64-encoded.</param>
/// <param name="SavedOn">When the draft was last written.</param>
public sealed record DraftState(
    int PageId,
    int VersionId,
    int VersionNumber,
    string ContentJson,
    string TemplateKey,
    int TemplateRevision,
    string RowVersion,
    DateTimeOffset? SavedOn);

/// <summary>
/// The outcome of writing to a draft.
/// </summary>
/// <param name="Draft">
/// The draft as it now stands. On a conflict this is the <em>stored</em> draft — the one that won —
/// so the editor can put it beside the caller's own copy and offer keep-mine, take-theirs, or a diff
/// (spec section 11.8).
/// </param>
/// <param name="Warnings">
/// Non-blocking diagnostics from the payload walk, such as a zone whose definition has since been
/// removed. A draft saves with these; only a publish is stopped by them turning into errors
/// (spec section 8.5).
/// </param>
/// <param name="ReferenceCount">
/// How many <c>ContentReference</c> rows the payload projected to, rewritten on every save. Reported
/// because a save that silently produced none is the shape of the stale-content failure in spec
/// section 7.3, and it is otherwise invisible.
/// </param>
public sealed record DraftSaveResult(
    DraftState Draft,
    IReadOnlyList<ApiDiagnostic> Warnings,
    int ReferenceCount);
