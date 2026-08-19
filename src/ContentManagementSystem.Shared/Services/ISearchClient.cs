using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// What the search and tag screens need, wherever they happen to be running (tasks P8-19, P8-20).
/// </summary>
/// <remarks>
/// Two implementations, like every other client here: one over HTTP for the WebAssembly backoffice,
/// and one over the services directly for pre-rendering — a request to itself would need a cookie it
/// does not have and an antiforgery token that has not been issued yet.
/// </remarks>
public interface ISearchClient
{
    /// <summary>Runs one search.</summary>
    /// <param name="query">Text, filters, and paging.</param>
    /// <param name="cancellationToken">Token observed while reading.</param>
    Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>Lists every tag with the number of pages carrying it.</summary>
    /// <param name="cancellationToken">Token observed while reading.</param>
    Task<IReadOnlyList<TagSummary>> GetTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>Suggests tags for what an editor has typed so far.</summary>
    /// <param name="prefix">What they have typed. Empty offers the most-used tags.</param>
    /// <param name="limit">Most suggestions to return.</param>
    /// <param name="cancellationToken">Token observed while reading.</param>
    Task<IReadOnlyList<TagSummary>> SuggestTagsAsync(
        string? prefix,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a tag everywhere, merging it if the new name is taken.</summary>
    /// <param name="id">The tag.</param>
    /// <param name="request">The new label.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<RenameTagResult>> RenameTagAsync(
        int id,
        RenameTagRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a tag and takes it off every page carrying it.</summary>
    /// <param name="id">The tag.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>How many pages lost the tag.</returns>
    Task<StructureClientResult<int>> DeleteTagAsync(int id, CancellationToken cancellationToken = default);
}
