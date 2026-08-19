namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// The searchable projection of one page, media item, or reusable item (spec section 17.1).
/// </summary>
/// <remarks>
/// Derived data, rebuilt from the source on every save and publish, and therefore excluded from
/// audit capture. Nothing here is authored: the body is what <c>IFieldType.ExtractSearchText</c>
/// made of a payload, which is why a search index rebuild is always safe and never loses anything.
/// <para>
/// It exists so that "find every page mentioning gearbox" is a full-text query rather than
/// <c>LIKE '%gearbox%'</c> over <c>ContentJson</c>, which cannot use an index and scans every
/// version of every page (risk R18).
/// </para>
/// </remarks>
public class SearchDocument : EntityBase
{
    /// <summary>Which kind of thing this describes.</summary>
    public SearchEntityKind EntityType { get; set; }

    /// <summary>Identity of that thing, within its kind.</summary>
    public int EntityId { get; set; }

    /// <summary>Title, weighted highest by the search ranking.</summary>
    public string Title { get; set; } = null!;

    /// <summary>Extracted body text, with no markup left in it.</summary>
    public string? Body { get; set; }

    /// <summary>
    /// Everything else worth matching on: slug, tags, alt text, file name.
    /// </summary>
    /// <remarks>
    /// A single column rather than one per source, because full-text search matches words and does
    /// not care which field they came from. Splitting it would buy per-field ranking the backoffice
    /// search screen does not offer.
    /// </remarks>
    public string? Keywords { get; set; }

    /// <summary>Where the thing lives, for rendering a result.</summary>
    public string? Url { get; set; }

    /// <summary>
    /// Whether the thing is currently published.
    /// </summary>
    /// <remarks>
    /// Carried on the projection so a future public search (spec section 17.2) is a filter on this
    /// column rather than a join back to the source tables — and so the backoffice can offer
    /// "unpublished only" without one either.
    /// </remarks>
    public bool IsPublished { get; set; }

    /// <summary>When this projection was last rebuilt.</summary>
    public DateTimeOffset UpdatedOn { get; set; }
}
