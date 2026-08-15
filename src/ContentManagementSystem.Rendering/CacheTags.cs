using System.Globalization;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Constructs the output-cache tag names of spec section 16.2.
/// </summary>
/// <remarks>
/// Tags are strings the eviction side has to spell identically, months and several phases apart:
/// the render adds <c>media:812</c> and a publish evicts by whatever the media service happens to
/// format. One place that builds them is what keeps those two in agreement, and it is why nothing
/// else in the rendering pipeline concatenates a tag by hand.
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
