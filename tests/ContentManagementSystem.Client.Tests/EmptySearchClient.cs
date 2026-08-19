using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Tests;

/// <summary>
/// An <see cref="ISearchClient"/> that reports an empty index and an empty vocabulary.
/// </summary>
/// <remarks>
/// Answers rather than throws, for the reason <see cref="SilentWorkflowClient"/> gives: the
/// properties panel asks for tag suggestions on every page it draws, so a stub that refused would
/// turn every editor test into a test of the tag box. A suite that <em>is</em> about tags overrides
/// the members it cares about.
/// </remarks>
public class EmptySearchClient : ISearchClient
{
    /// <inheritdoc />
    public virtual Task<SearchResults> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SearchResults([], 0, FullText: false));

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TagSummary>> GetTagsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TagSummary>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TagSummary>> SuggestTagsAsync(
        string? prefix,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TagSummary>>([]);

    /// <inheritdoc />
    public virtual Task<StructureClientResult<RenameTagResult>> RenameTagAsync(
        int id,
        RenameTagRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This suite does not rename tags.");

    /// <inheritdoc />
    public virtual Task<StructureClientResult<int>> DeleteTagAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This suite does not delete tags.");
}
