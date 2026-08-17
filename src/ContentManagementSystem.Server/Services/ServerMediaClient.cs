using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Library;
using ContentManagementSystem.Core.Media.Upload;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="IMediaClient"/>, over the services directly (task P5-22).
/// </summary>
/// <param name="library">Metadata, edits, and the lifecycle.</param>
/// <param name="folders">The organizing tree.</param>
/// <param name="uploads">The pipeline every byte enters the library through.</param>
/// <param name="signer">Signs the URLs a screen shows an item at.</param>
/// <param name="gate">Keeps concurrently initializing components off each other's database work.</param>
/// <remarks>
/// Used during pre-rendering, so the media browser arrives with its first page of items already in
/// the HTML. It calls the services rather than looping back through the HTTP API — a request to
/// itself would need a cookie it does not have and an antiforgery token that has not been issued yet.
/// <para>
/// Authorization is unaffected by the shortcut: every service here checks the caller's permissions
/// itself, against the same request principal the API would have seen.
/// </para>
/// <para>
/// <strong>Uploads take the single-request path whatever their size.</strong> Chunking exists to
/// survive a connection dropping between a browser and this server; there is no connection here, so
/// staging parts in the store and reading them straight back would be cost with nothing bought.
/// </para>
/// </remarks>
public sealed class ServerMediaClient(
    IMediaLibraryService library,
    IMediaFolderService folders,
    IMediaUploadService uploads,
    IMediaUrlSigner signer,
    PrerenderGate gate) : IMediaClient
{
    /// <inheritdoc />
    public async Task<MediaListResult> ListAsync(
        MediaQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return (await gate.RunAsync(token => library.ListAsync(query, token), cancellationToken)).Value
            ?? new MediaListResult([], 0, query.Skip, query.Take);
    }

    /// <inheritdoc />
    public async Task<MediaDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => library.GetAsync(id, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, MediaLinks>> LinksAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var links = new Dictionary<int, MediaLinks>();

        foreach (var id in ids.Distinct())
        {
            var item = await gate.RunAsync(token => library.GetAsync(id, token), cancellationToken);

            if (item.Value is { } detail) links[id] = MediaLinkFactory.For(detail, signer);
        }

        return links;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaFolderNode>> FoldersAsync(
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => folders.ListAsync(token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaFolderNode>> CreateFolderAsync(
        CreateMediaFolderRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => folders.CreateAsync(request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaUploadResult>> UploadAsync(
        MediaUploadContent content,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var result = await gate.RunAsync(token => uploads.UploadAsync(ToRequest(content), token), cancellationToken);

        progress?.Report(1);

        return Project(result);
    }

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaUploadResult>> ReplaceAsync(
        int id,
        MediaUploadContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Project(await gate.RunAsync(
            token => uploads.ReplaceAsync(id, ToRequest(content), token),
            cancellationToken));
    }

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaDetail>> PatchAsync(
        int id,
        PatchMediaRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => library.PatchAsync(id, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaDetail>> SetEditsAsync(
        int id,
        SetMediaEditsRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => library.SetEditsAsync(id, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaDetail>> RevertEditsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => library.RevertEditsAsync(id, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => library.DeleteAsync(id, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaDetail>> RestoreAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => library.RestoreAsync(id, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaDeleteResult>> PurgeAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => library.PurgeAsync(id, token), cancellationToken));

    /// <inheritdoc />
    public async Task<ReferenceImpact> WhereUsedAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(
            token => library.WhereUsedAsync(id, token),
            cancellationToken)).Value ?? ReferenceImpact.None;

    private static MediaUploadRequest ToRequest(MediaUploadContent content) => new(
        content.Content,
        content.FileName,
        content.FolderId,
        content.AltText,
        content.IsDecorative,
        content.Title,
        content.Caption,
        content.Credit);

    /// <summary>
    /// Reduces a service result to what a screen needs.
    /// </summary>
    /// <remarks>
    /// The outcome enum is dropped deliberately: it exists to choose an HTTP status, and there is no
    /// response here to give one to. What survives is the pair a screen actually renders — the value
    /// and the diagnostics — which is exactly what the HTTP client parses back out of a problem body.
    /// </remarks>
    private static StructureClientResult<T> Project<T>(CmsResult<T> result) =>
        result.IsSuccess && result.Value is not null
            ? StructureClientResult<T>.Success(
                result.Value,
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning))
            : StructureClientResult<T>.Failure(
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
}
