using ContentManagementSystem.Shared.Contracts.Search;

namespace ContentManagementSystem.Core.Search;

/// <summary>
/// The backoffice search box and its filters (task P8-19, spec section 17.1).
/// </summary>
/// <remarks>
/// Reads the index rather than the content tables, which is the whole point of having one: "find
/// every page mentioning gearbox" is a full-text probe here and a scan over every version of every
/// payload anywhere else (risk R18).
/// </remarks>
public interface ISearchService
{
    /// <summary>
    /// Runs one search.
    /// </summary>
    /// <param name="query">Text, filters, and paging.</param>
    /// <param name="cancellationToken">Token observed throughout.</param>
    /// <returns>The hits, most relevant first.</returns>
    /// <remarks>
    /// Results are cut by the caller's access rules after the page is taken, the way every list
    /// endpoint does it: the precedence rules are "deeper beats shallower, deny beats allow", and
    /// pushing that into SQL would mean keeping two copies of them in step. The cost is that a page
    /// of hits can come back shorter than the count beside it.
    /// </remarks>
    Task<CmsResult<SearchResults>> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default);
}
