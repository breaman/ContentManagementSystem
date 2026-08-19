using System.Globalization;

namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// Constructs the output-cache tag names of spec section 16.2.
/// </summary>
/// <remarks>
/// Tags are strings the eviction side has to spell identically, months and several phases apart:
/// the render adds <c>media:812</c> and a publish evicts by whatever the media service happens to
/// format. One place that builds them is what keeps those two in agreement, and it is why nothing
/// else concatenates a tag by hand.
/// <para>
/// In Core rather than in Rendering, even though rendering is where tags are collected, because the
/// half that evicts them lives here — and two copies of this file, one per side, is precisely the
/// disagreement it exists to prevent.
/// </para>
/// </remarks>
public static class CacheTags
{
    /// <summary>The tag every rendered page carries, for a manual purge-all.</summary>
    public const string All = "content";

    /// <summary>Tags a page's own response. Evicted when it is published, moved, or deleted.</summary>
    /// <param name="pageId">The page id.</param>
    /// <returns>The tag.</returns>
    public static string Page(int pageId) => Format("page", pageId);

    /// <summary>Tags every page rendering a reusable item.</summary>
    /// <param name="reusableId">The reusable content item's id.</param>
    /// <returns>The tag.</returns>
    public static string Reusable(int reusableId) => Format("ru", reusableId);

    /// <summary>Tags every page rendering a media item.</summary>
    /// <param name="mediaId">The media item's id.</param>
    /// <returns>The tag.</returns>
    public static string Media(int mediaId) => Format("media", mediaId);

    /// <summary>Tags every page built on a template.</summary>
    /// <param name="templateId">The template id.</param>
    /// <returns>The tag.</returns>
    public static string Template(int templateId) => Format("tpl", templateId);

    /// <summary>
    /// Menu key the tree-generated navigation is tagged under (spec section 10.7).
    /// </summary>
    /// <remarks>
    /// Structural navigation has no menu row and therefore no key of its own, but it is still a
    /// dependency a page takes and still has to be evicted when the tree changes. Naming it here
    /// rather than at the two call sites keeps it out of the space a managed menu's key could
    /// collide with — a menu called <c>tree</c> is a menu somebody would create.
    /// </remarks>
    public const string StructuralMenuKey = "*tree";

    /// <summary>Tags every page rendering a navigation menu.</summary>
    /// <param name="menuKey">The menu key.</param>
    /// <returns>The tag.</returns>
    public static string Navigation(string menuKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuKey);

        return $"nav:{menuKey}";
    }

    // Invariant culture explicitly: a tag is an identifier compared byte for byte by the cache
    // store, and a culture that formats integers with group separators would produce a tag the
    // eviction side never matches.
    private static string Format(string prefix, int id) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}:{id}");
}
