using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Tests;

/// <summary>
/// An <see cref="IMediaClient"/> whose every member refuses until a test overrides it.
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="StubPageClient"/>, for the same reason.
/// </remarks>
public abstract class StubMediaClient : IMediaClient
{
    /// <inheritdoc />
    public virtual Task<MediaListResult> ListAsync(
        MediaQuery query,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<MediaDetail?> GetAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyDictionary<int, MediaLinks>> LinksAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<MediaFolderNode>> FoldersAsync(
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaFolderNode>> CreateFolderAsync(
        CreateMediaFolderRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaUploadResult>> UploadAsync(
        MediaUploadContent content,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaUploadResult>> ReplaceAsync(
        int id,
        MediaUploadContent content,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaDetail>> PatchAsync(
        int id,
        PatchMediaRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaDetail>> SetEditsAsync(
        int id,
        SetMediaEditsRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaDetail>> RevertEditsAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaDetail>> RestoreAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<MediaDeleteResult>> PurgeAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<ReferenceImpact> WhereUsedAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    private static NotSupportedException Unexpected() =>
        new("The component under test called a media client member the test did not stub.");
}
