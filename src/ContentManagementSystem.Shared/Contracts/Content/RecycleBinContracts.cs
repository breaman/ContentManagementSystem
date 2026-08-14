using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// One page in the recycle bin (spec section 14.10).
/// </summary>
/// <param name="Id">Identity of the page.</param>
/// <param name="Title">Title as at its draft, so the list reads as content rather than as ids.</param>
/// <param name="Slug">The URL segment it held.</param>
/// <param name="ParentId">Its former parent, which may itself be deleted.</param>
/// <param name="IsSubtreeRoot">
/// Whether it was deleted in its own right rather than swept up as somebody's descendant. The bin
/// lists these and offers the subtree beneath them, so one delete of a section reads as one entry.
/// </param>
/// <param name="DescendantCount">How many deleted pages sit beneath it.</param>
/// <param name="WasPublished">Whether the page was live when it was deleted.</param>
/// <param name="DeletedOn">When it was deleted.</param>
/// <param name="DeletedBy">Who deleted it.</param>
public sealed record RecycleBinEntry(
    int Id,
    string Title,
    string Slug,
    int? ParentId,
    bool IsSubtreeRoot,
    int DescendantCount,
    bool WasPublished,
    DateTimeOffset? DeletedOn,
    int? DeletedBy);

/// <summary>
/// What a soft delete or a restore did to the tree.
/// </summary>
/// <param name="PageId">The page the operation was asked about.</param>
/// <param name="AffectedPageIds">
/// Every page moved, the named one first. A delete takes the whole subtree with it and a restore
/// brings the whole subtree back, so the count is what a confirmation dialog has to show before
/// anybody agrees to it.
/// </param>
/// <param name="UnpublishedCount">How many of them were live and have been retired.</param>
/// <param name="Warnings">
/// Non-blocking diagnostics — most usefully that a restored page's former parent is still deleted,
/// so it has come back at the site root instead.
/// </param>
public sealed record SubtreeResult(
    int PageId,
    IReadOnlyList<int> AffectedPageIds,
    int UnpublishedCount,
    IReadOnlyList<ApiDiagnostic> Warnings);

/// <summary>
/// Why a permanent delete was refused: something still points at the page.
/// </summary>
/// <param name="PageId">The page that cannot be removed.</param>
/// <param name="ReferencingPageIds">Pages whose stored content links to it.</param>
/// <remarks>
/// Named rather than counted, because "3 pages reference this" is not something an editor can act
/// on. The rows come from <c>ContentReference</c>, which over-reports by design (spec section 7.3) —
/// so this refusal is occasionally cautious, and that is the direction to be wrong in when the
/// alternative is irreversible.
/// </remarks>
public sealed record ReferenceBlockage(int PageId, IReadOnlyList<int> ReferencingPageIds);
