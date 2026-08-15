using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Body of <c>POST /api/cms/v1/pages/{id}/publish</c>.
/// </summary>
/// <param name="AcknowledgeWarnings">
/// Whether the caller has seen the non-blocking diagnostics and still wants to proceed.
/// </param>
/// <remarks>
/// Defaults to <see langword="false"/>, and an omitted body means the same thing. A first attempt
/// carrying warnings is refused with <c>422</c> and the warnings in it; the client shows them and
/// resubmits with this set (spec section 22.2). The point is that an unattended caller — a scheduled
/// job, a script — cannot publish past a problem a person would have stopped to look at.
/// <para>
/// A flag rather than spec section 22.2's <c>acknowledgedWarnings</c> array of codes. Listing codes
/// looks more precise and is not: the set of warnings can change between the check and the publish,
/// so a client would be acknowledging a list it may no longer be looking at, and the honest question
/// is whether a person saw the warnings this attempt produced.
/// </para>
/// </remarks>
public sealed record PublishPageRequest(bool AcknowledgeWarnings = false);

/// <summary>
/// What a dry-run publish check found, as <c>POST /pages/{id}/validate</c> returns it.
/// </summary>
/// <param name="CanPublish">Whether a publish attempted now would be accepted.</param>
/// <param name="Errors">Everything blocking, each naming the zone, block, and property at fault.</param>
/// <param name="Warnings">
/// Everything worth showing but not blocking — an orphaned zone, a block type no longer deployed.
/// The publish endpoint accepts these once the client has acknowledged them (spec section 14.6).
/// </param>
/// <remarks>
/// The same code path a real publish runs, stopped before the transaction. A separate implementation
/// would eventually disagree with the real one, and the direction it disagrees in is a green check
/// followed by a refused publish.
/// </remarks>
public sealed record PublishValidation(
    bool CanPublish,
    IReadOnlyList<ApiDiagnostic> Errors,
    IReadOnlyList<ApiDiagnostic> Warnings);

/// <summary>
/// What a publish did.
/// </summary>
/// <param name="PageId">Page published.</param>
/// <param name="VersionId">Identity of the new immutable version.</param>
/// <param name="VersionNumber">Its version number.</param>
/// <param name="PublishedOn">When it went live.</param>
/// <param name="ArchivedVersionNumber">
/// The version it superseded, or null on a first publish. Reported so the confirmation can say what
/// was replaced rather than only what is now live.
/// </param>
/// <param name="ReferenceCount">
/// How many <c>ContentReference</c> rows the published payload projected to. A published version
/// with none, where the draft had some, is the stale-content failure in spec section 7.3 arriving
/// silently, and this is the only place it is visible.
/// </param>
/// <param name="Warnings">Non-blocking diagnostics the publish went ahead despite.</param>
public sealed record PublishResult(
    int PageId,
    int VersionId,
    int VersionNumber,
    DateTimeOffset PublishedOn,
    int? ArchivedVersionNumber,
    int ReferenceCount,
    IReadOnlyList<ApiDiagnostic> Warnings);

/// <summary>
/// What an unpublish retired.
/// </summary>
/// <param name="PageId">Page retired from the public site.</param>
/// <param name="UnpublishedVersionNumber">
/// The version that stopped being served. Reported rather than answering with no content, so the
/// confirmation can name what visitors will no longer see instead of only saying that something
/// happened.
/// </param>
public sealed record UnpublishResult(int PageId, int UnpublishedVersionNumber);
