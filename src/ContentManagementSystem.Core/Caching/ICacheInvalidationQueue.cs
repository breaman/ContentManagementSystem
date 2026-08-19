namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// Enqueues cache invalidation into the transaction that caused it (task P8-09, spec section 16.3).
/// </summary>
/// <remarks>
/// Every method here <em>adds a row to the caller's <c>DbContext</c> and saves nothing</em>. That is
/// the whole design: the message is written by the same <c>SaveChanges</c> that writes the publish,
/// inside the same transaction, so a publish that rolls back leaves no invalidation behind and a
/// publish that commits always has one waiting — including when the process dies in the instant
/// between (acceptance criterion P8 #8).
/// <para>
/// A caller that enqueues and then does not save has enqueued nothing, which is the correct
/// behaviour and the reason nothing here returns a handle to something that must be flushed.
/// </para>
/// </remarks>
public interface ICacheInvalidationQueue
{
    /// <summary>
    /// Enqueues the eviction for a page that was published, unpublished, deleted, or restored.
    /// </summary>
    /// <param name="pageId">The page.</param>
    /// <param name="cancellationToken">Token observed while looking up affected menus.</param>
    /// <remarks>
    /// The page's own tag, the tree navigation, and any managed menu that names it. The menus are a
    /// query rather than an assumption: a footer linking to this page renders its title, so a page
    /// that changes changes every page showing that footer.
    /// </remarks>
    Task EnqueuePageAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues the eviction for several pages at once, as a move or a bulk operation produces.
    /// </summary>
    /// <param name="pageIds">The pages, in any order and with any repeats.</param>
    /// <param name="cancellationToken">Token observed while looking up affected menus.</param>
    Task EnqueuePagesAsync(IEnumerable<int> pageIds, CancellationToken cancellationToken = default);

    /// <summary>Enqueues the eviction of every page rendering a reusable item.</summary>
    /// <param name="reusableContentId">The reusable item.</param>
    void EnqueueReusable(int reusableContentId);

    /// <summary>Enqueues the eviction of every page rendering a media item.</summary>
    /// <param name="mediaItemId">The media item.</param>
    void EnqueueMedia(int mediaItemId);

    /// <summary>Enqueues the eviction of every page built on a template.</summary>
    /// <param name="templateId">The template.</param>
    void EnqueueTemplate(int templateId);

    /// <summary>Enqueues an eviction of tags built elsewhere.</summary>
    /// <param name="tags">The tags, as <see cref="CacheTags"/> spells them.</param>
    void Enqueue(IReadOnlyCollection<string> tags);
}
