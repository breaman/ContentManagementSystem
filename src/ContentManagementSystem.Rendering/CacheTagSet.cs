namespace ContentManagementSystem.Rendering;

/// <summary>
/// The output-cache tags accumulated while one page renders (spec sections 15.2 and 16.2).
/// </summary>
/// <remarks>
/// Invalidation is derived from what was actually rendered rather than from a hand-maintained list:
/// a zone that resolves reusable content adds <c>ru:{id}</c> as it resolves it, a media field adds
/// <c>media:{id}</c>, and the class of bug where a developer forgets to declare a dependency
/// disappears.
/// <para>
/// <strong>One instance per render, never shared.</strong> A set reused across requests would
/// accumulate one visitor's dependencies onto another visitor's response and evict — or fail to
/// evict — the wrong pages. <see cref="RenderContext"/> creates one per render, and the published
/// content cache holds no reference to it precisely so this cannot be cached alongside the content
/// it describes.
/// </para>
/// <para>
/// Spec section 15.2 types this member as <c>ISet&lt;string&gt;</c>. It is a class of its own here
/// so that adding a tag goes through <see cref="CacheTags"/> rather than through string
/// concatenation at a dozen call sites, which is the failure the tag scheme exists to prevent.
/// </para>
/// </remarks>
public sealed class CacheTagSet
{
    // Static SSR renders on the renderer's synchronization context, so tags added by field
    // renderers are already serialized. The lock costs nothing measurable per page and removes the
    // need for every future renderer author to know that.
    private readonly Lock _gate = new();

    private readonly HashSet<string> _tags = new(StringComparer.Ordinal);

    /// <summary>How many distinct tags have been collected.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _tags.Count;
            }
        }
    }

    /// <summary>Adds a tag, ignoring one that is already present.</summary>
    /// <param name="tag">The tag, built with <see cref="CacheTags"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is null or blank.</exception>
    public void Add(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        lock (_gate)
        {
            _tags.Add(tag);
        }
    }

    /// <summary>Adds the tag for a media item this render depends on.</summary>
    /// <param name="mediaId">The media item's id.</param>
    public void AddMedia(int mediaId) => Add(CacheTags.Media(mediaId));

    /// <summary>Adds the tag for a reusable content item this render depends on.</summary>
    /// <param name="reusableId">The reusable item's id.</param>
    public void AddReusable(int reusableId) => Add(CacheTags.Reusable(reusableId));

    /// <summary>Adds the tag for a page this render depends on.</summary>
    /// <param name="pageId">The page id.</param>
    public void AddPage(int pageId) => Add(CacheTags.Page(pageId));

    /// <summary>Adds the tag for a navigation menu this render depends on.</summary>
    /// <param name="menuKey">The menu key.</param>
    public void AddNavigation(string menuKey) => Add(CacheTags.Navigation(menuKey));

    /// <summary>Whether a tag has been collected.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns><see langword="true"/> when it is present.</returns>
    public bool Contains(string tag)
    {
        lock (_gate)
        {
            return _tags.Contains(tag);
        }
    }

    /// <summary>
    /// Takes a snapshot of the tags collected so far, sorted so the set is stable to assert on.
    /// </summary>
    /// <returns>The tags.</returns>
    /// <remarks>
    /// Read this <em>after</em> the render completes. Tags accumulate during rendering, so a
    /// response that sends its headers before the render finishes carries an incomplete set and
    /// produces a page that never invalidates — which is why delivery renders to a buffer, then sets
    /// headers, then writes (S2 spike, consequence 3).
    /// </remarks>
    public string[] ToArray()
    {
        lock (_gate)
        {
            var tags = _tags.ToArray();
            Array.Sort(tags, StringComparer.Ordinal);

            return tags;
        }
    }
}
