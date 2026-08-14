using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Content;

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
