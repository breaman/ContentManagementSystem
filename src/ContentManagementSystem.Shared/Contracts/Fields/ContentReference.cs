namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// The kind of entity a stored content value points at.
/// </summary>
public enum ContentReferenceTargetType
{
    /// <summary>Another page in the content tree.</summary>
    Page = 0,

    /// <summary>An item in the media library.</summary>
    Media = 1,

    /// <summary>An independently published reusable content item.</summary>
    ReusableContent = 2,
}

/// <summary>
/// One edge from a content value to an entity it depends on, discovered by walking a payload.
/// </summary>
/// <param name="TargetType">Kind of entity referenced.</param>
/// <param name="TargetId">Identity of the referenced entity.</param>
/// <param name="Path">
/// Payload path the reference was found at, such as <c>zones.hero[0].properties.image</c>, built up
/// as the value is visited. A field type reports a path relative to its own value — null when the
/// value as a whole is the reference, <c>items[2]</c> for the third image of a gallery — because it
/// cannot know where in the document it sits, and the schema walk prefixes the rest.
/// </param>
/// <remarks>
/// These edges are projected into relational rows on every save and publish, which is what makes
/// where-used, link integrity, cache invalidation, and orphan detection answerable with an indexed
/// query rather than a scan over JSON (spec section 6.2).
/// <para>
/// The one rule that matters: a field type must report <em>every</em> entity its value points at.
/// An omission produces a page that silently fails to invalidate when its dependency changes, and
/// stale content is very hard to diagnose after the fact (spec section 7.3).
/// </para>
/// </remarks>
public readonly record struct ContentReference(
    ContentReferenceTargetType TargetType,
    int TargetId,
    string? Path = null);
