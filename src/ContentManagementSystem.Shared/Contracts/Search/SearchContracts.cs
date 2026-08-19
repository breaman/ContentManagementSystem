namespace ContentManagementSystem.Shared.Contracts.Search;

/// <summary>What kind of thing a search hit is (spec section 17.1).</summary>
/// <remarks>
/// The contract's own copy of <c>SearchEntityKind</c>. The stored enum is a database value whose
/// numbers are part of the schema; this one is what an API answers with, and keeping them separate
/// is what lets either be renamed without the other becoming a migration.
/// </remarks>
public enum SearchResultKind
{
    /// <summary>A page.</summary>
    Page = 0,

    /// <summary>A media library item.</summary>
    Media = 1,

    /// <summary>A reusable content item.</summary>
    Reusable = 2,
}

/// <summary>
/// Everything the backoffice search screen can ask for (task P8-19, spec section 17.1).
/// </summary>
/// <param name="Text">
/// Free text over title, extracted body, and keywords. Null or empty is a legitimate query: the
/// filters alone answer "every page owned by me that is past its review date".
/// </param>
/// <param name="Kind">Restricts to one kind of content. Null searches all three.</param>
/// <param name="TemplateId">Restricts to pages laid out by one template.</param>
/// <param name="Status">
/// Restricts to pages whose draft sits at one lifecycle status, matched against
/// <c>PageVersionStatus</c> by name, case-insensitively. An unrecognised value is refused rather
/// than silently ignored.
/// </param>
/// <param name="OwnerUserId">Restricts to pages one editor owns.</param>
/// <param name="Tag">Restricts to pages carrying one tag, by slug or by name.</param>
/// <param name="ModifiedFrom">Only things changed at or after this instant.</param>
/// <param name="ModifiedTo">Only things changed at or before this instant.</param>
/// <param name="HasUnpublishedChanges">
/// True for pages whose draft has moved on from what is published — the "needs publishing" list.
/// False for pages where the two agree.
/// </param>
/// <param name="PastReviewDate">True for pages whose review date has gone by.</param>
/// <param name="Skip">How many hits to step over.</param>
/// <param name="Limit">How many hits to return, clamped by the service.</param>
/// <remarks>
/// <strong>Every filter but <see cref="Text"/>, <see cref="Kind"/>, and the modified range is a page
/// filter</strong>, because templates, owners, review dates and tags exist on pages and nowhere
/// else. Setting one restricts the result to pages rather than quietly matching every media item as
/// well, which is the reading an editor who typed one into the box already has.
/// </remarks>
public sealed record SearchQuery(
    string? Text = null,
    SearchResultKind? Kind = null,
    int? TemplateId = null,
    string? Status = null,
    int? OwnerUserId = null,
    string? Tag = null,
    DateTimeOffset? ModifiedFrom = null,
    DateTimeOffset? ModifiedTo = null,
    bool? HasUnpublishedChanges = null,
    bool PastReviewDate = false,
    int Skip = 0,
    int? Limit = null);

/// <summary>One thing the search matched.</summary>
/// <param name="Kind">What sort of thing it is.</param>
/// <param name="Id">Its identity within that sort.</param>
/// <param name="Title">Its title, as the index holds it.</param>
/// <param name="Url">Its public URL, or null for something that has none.</param>
/// <param name="Excerpt">The opening of its indexed text, for showing under the title.</param>
/// <param name="IsPublished">Whether it is currently published.</param>
/// <param name="UpdatedOn">When the index last rebuilt this entry.</param>
public sealed record SearchHit(
    SearchResultKind Kind,
    int Id,
    string Title,
    string? Url,
    string? Excerpt,
    bool IsPublished,
    DateTimeOffset UpdatedOn);

/// <summary>A page of search hits.</summary>
/// <param name="Hits">The hits, most relevant first.</param>
/// <param name="TotalCount">How many the query matched in total.</param>
/// <param name="FullText">
/// Whether the full-text engine answered this query, or whether it fell back to a scan.
/// </param>
/// <remarks>
/// <see cref="FullText"/> is reported rather than hidden. The fallback is correct and slow, and a
/// deployment running on an engine without full-text installed should be able to see that from the
/// screen rather than from a stopwatch (spec section 17.1, risk R18).
/// </remarks>
public sealed record SearchResults(
    IReadOnlyList<SearchHit> Hits,
    int TotalCount,
    bool FullText);

/// <summary>Error codes the search endpoints answer with.</summary>
public static class SearchCodes
{
    /// <summary>The caller may not do this.</summary>
    public const string Forbidden = "search.forbidden";

    /// <summary>The status filter named a lifecycle state that does not exist.</summary>
    public const string UnknownStatus = "search.unknownStatus";
}
