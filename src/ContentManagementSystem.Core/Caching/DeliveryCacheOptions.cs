namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// Lifetimes for the caches the delivery path reads through (spec section 16.1).
/// </summary>
public sealed class DeliveryCacheOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Cms:Cache";

    /// <summary>Whether published content and route lookups are cached at all.</summary>
    /// <remarks>
    /// A switch worth having: it turns the delivery path back into "read the database every time",
    /// which is how a suspected staleness bug is diagnosed without a deployment.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes a published version stays in the content cache.</summary>
    /// <remarks>
    /// Fifteen, from spec section 16.1. It is a backstop rather than the mechanism: entries are
    /// evicted by tag when the page is published, and this is what bounds the damage of an
    /// invalidation that never arrived.
    /// </remarks>
    public int ContentMinutes { get; set; } = 15;

    /// <summary>Minutes a URL-to-page lookup stays cached.</summary>
    public int RouteMinutes { get; set; } = 15;

    /// <summary>Minutes a cached page response stays in the output cache.</summary>
    /// <remarks>
    /// An hour, per spec section 16.1, and it is the backstop of task P8-12: any invalidation that
    /// was missed self-heals within it rather than leaving a page stale until somebody edits it
    /// again.
    /// </remarks>
    public int OutputMinutes { get; set; } = 60;
}
