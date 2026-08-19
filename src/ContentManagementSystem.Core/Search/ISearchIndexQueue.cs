using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Search;

/// <summary>
/// Enqueues search indexing into the transaction that caused it (task P8-18, spec section 17.1).
/// </summary>
/// <remarks>
/// The same shape as <c>ICacheInvalidationQueue</c>, and for the same reason: every method here adds
/// a row to the caller's <c>DbContext</c> and saves nothing, so the request to index commits with
/// the save that made it necessary or not at all. A save that rolls back leaves no index message
/// describing content that was never written.
/// <para>
/// <strong>Indexing is asynchronous on purpose.</strong> Extracting text from every zone of a
/// payload is real work, and doing it inside the save would put it on the path an editor waits on —
/// for a projection nobody reads until they search. The cost of that choice is a window where a
/// just-saved page is not yet findable, which is seconds wide and is what the nightly reconcile
/// backstops (risk R18).
/// </para>
/// </remarks>
public interface ISearchIndexQueue
{
    /// <summary>Enqueues one page for indexing.</summary>
    /// <param name="pageId">The page.</param>
    void EnqueuePage(int pageId);

    /// <summary>Enqueues several pages at once, as a move or a bulk operation produces.</summary>
    /// <param name="pageIds">The pages, in any order and with any repeats.</param>
    void EnqueuePages(IEnumerable<int> pageIds);

    /// <summary>Enqueues one media item.</summary>
    /// <param name="mediaItemId">The media item.</param>
    void EnqueueMedia(int mediaItemId);

    /// <summary>Enqueues one reusable content item.</summary>
    /// <param name="reusableContentId">The reusable item.</param>
    void EnqueueReusable(int reusableContentId);

    /// <summary>Enqueues things of one kind.</summary>
    /// <param name="kind">What sort of thing the ids name.</param>
    /// <param name="entityIds">The things, in any order and with any repeats.</param>
    /// <remarks>
    /// A deletion needs no message of its own: the indexer reads the source when it runs, finds it
    /// gone, and removes the document. One code path covers "changed" and "no longer there", which
    /// is what stops a recycled page staying in the index because somebody added a case and forgot
    /// the other one.
    /// </remarks>
    void Enqueue(SearchEntityKind kind, IEnumerable<int> entityIds);
}
