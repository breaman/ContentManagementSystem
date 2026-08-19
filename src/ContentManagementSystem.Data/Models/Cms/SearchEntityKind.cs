namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>What kind of thing a <see cref="SearchDocument"/> describes (spec section 17.1).</summary>
/// <remarks>
/// One index over three kinds of content rather than three indexes, because the backoffice search
/// box asks one question — "where have I seen this word" — and answering it from three tables would
/// mean three queries and an arbitrary way of interleaving their results.
/// </remarks>
public enum SearchEntityKind
{
    /// <summary>A page.</summary>
    Page = 0,

    /// <summary>A media library item.</summary>
    Media = 1,

    /// <summary>A reusable content item.</summary>
    Reusable = 2,
}
