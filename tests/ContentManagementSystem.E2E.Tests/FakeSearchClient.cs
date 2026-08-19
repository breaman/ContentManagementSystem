using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Feeds the properties panel a small tag vocabulary so the accessibility gate has chips to check
/// (tasks P8-19, P8-20, P6-36).
/// </summary>
/// <remarks>
/// Non-empty on purpose, like the other fakes here: axe has nothing to say about a tag box with no
/// tags in it, and the parts of that control worth judging — the removal buttons' accessible names
/// and the completion list bound to the input — only exist once there are some.
/// </remarks>
public sealed class FakeSearchClient : ISearchClient
{
    private static readonly TagSummary[] Vocabulary =
    [
        new(1, "Product docs", "product-docs", 12),
        new(2, "Release notes", "release-notes", 4),
    ];

    /// <inheritdoc />
    public Task<SearchResults> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SearchResults(
            [new SearchHit(SearchResultKind.Page, 1, "What our plans cost", "/plans/pricing",
                "Everything about our prices, per plan.", true, DateTimeOffset.UtcNow)],
            1,
            FullText: true));

    /// <inheritdoc />
    public Task<IReadOnlyList<TagSummary>> GetTagsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TagSummary>>(Vocabulary);

    /// <inheritdoc />
    public Task<IReadOnlyList<TagSummary>> SuggestTagsAsync(
        string? prefix,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TagSummary>>(Vocabulary);

    /// <inheritdoc />
    public Task<StructureClientResult<RenameTagResult>> RenameTagAsync(
        int id,
        RenameTagRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("These gates render screens; they do not write.");

    /// <inheritdoc />
    public Task<StructureClientResult<int>> DeleteTagAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("These gates render screens; they do not write.");
}
