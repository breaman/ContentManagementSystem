using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Search;

/// <summary>
/// Rebuilds <c>SearchDocument</c> rows from the content they describe (task P8-18, spec section 17.1).
/// </summary>
/// <remarks>
/// Everything here is a rebuild rather than an edit: the document is derived, so re-running the
/// indexer over anything is always safe and never loses authored data. That is what makes the
/// nightly reconcile a legitimate repair rather than a risky one.
/// </remarks>
public interface ISearchIndexer
{
    /// <summary>
    /// Rebuilds the index entries for some things of one kind.
    /// </summary>
    /// <param name="kind">What sort of thing the ids name.</param>
    /// <param name="entityIds">The things, in any order and with any repeats.</param>
    /// <param name="cancellationToken">Token observed throughout.</param>
    /// <returns>How many documents were written or removed.</returns>
    /// <remarks>
    /// An id whose source no longer exists — recycled, hard-deleted, or never there — has its
    /// document removed. "Index this" and "this is gone" are the same call because the indexer reads
    /// the source rather than being told what happened to it.
    /// </remarks>
    Task<int> IndexAsync(
        SearchEntityKind kind,
        IReadOnlyList<int> entityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every page, media item, and reusable item, and repairs whatever the index has wrong.
    /// </summary>
    /// <param name="cancellationToken">Token observed throughout.</param>
    /// <returns>What it found and what it did about it.</returns>
    /// <remarks>
    /// The backstop for risk R18. Indexing is asynchronous, so a message that was dropped, a process
    /// that died mid-batch, or a code path that forgot to enqueue leaves the index quietly wrong —
    /// quietly, because a missing search result looks exactly like a page that does not mention the
    /// word. Running this nightly turns "wrong until somebody notices" into "wrong until tomorrow".
    /// </remarks>
    Task<SearchReconcileReport> ReconcileAsync(CancellationToken cancellationToken = default);
}

/// <summary>What one reconcile pass did (task P8-18).</summary>
/// <param name="Rebuilt">Documents written because they were missing or older than their source.</param>
/// <param name="Removed">Documents deleted because the thing they described is gone.</param>
/// <param name="Examined">How many live things were considered.</param>
public sealed record SearchReconcileReport(int Rebuilt, int Removed, int Examined)
{
    /// <summary>Whether the pass had to change anything.</summary>
    /// <remarks>
    /// A pass that changes nothing is the expected result on a healthy site, and is worth being able
    /// to say outright: it is the evidence that the outbox path is keeping up.
    /// </remarks>
    public bool FoundNothingWrong => Rebuilt == 0 && Removed == 0;
}
