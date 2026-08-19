using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Search;
using ContentManagementSystem.Core.Tags;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="ISearchClient"/>, over the services directly (tasks P8-19, P8-20).
/// </summary>
/// <param name="search">The search index reader.</param>
/// <param name="tags">The tag vocabulary.</param>
/// <param name="gate">Keeps concurrently initializing components off each other's database work.</param>
/// <remarks>
/// Used during pre-rendering, so a bookmarked search URL arrives with its results in the HTML rather
/// than showing a spinner until the WebAssembly runtime has downloaded. Both services check the
/// caller's permissions themselves, so the shortcut past the API changes nothing about who may do
/// what.
/// </remarks>
public sealed class ServerSearchClient(ISearchService search, ITagService tags, PrerenderGate gate)
    : ISearchClient
{
    /// <inheritdoc />
    public async Task<SearchResults> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => search.SearchAsync(query, token), cancellationToken)).Value
        ?? new SearchResults([], 0, FullText: false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagSummary>> GetTagsAsync(
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => tags.ListAsync(token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagSummary>> SuggestTagsAsync(
        string? prefix,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => tags.SuggestAsync(prefix, limit, token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<StructureClientResult<RenameTagResult>> RenameTagAsync(
        int id,
        RenameTagRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => tags.RenameAsync(id, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<int>> DeleteTagAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => tags.DeleteAsync(id, token), cancellationToken));

    /// <summary>Narrows a service result to what a screen needs from it.</summary>
    private static StructureClientResult<T> Project<T>(CmsResult<T> result) =>
        result.IsSuccess
            ? StructureClientResult<T>.Success(
                result.Value!,
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning))
            : StructureClientResult<T>.Failure(
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
}
