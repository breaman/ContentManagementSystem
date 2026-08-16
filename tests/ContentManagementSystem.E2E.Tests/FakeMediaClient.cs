using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Feeds the media screens and the media picker a fixed library so the accessibility gate has
/// markup to check (tasks P5-19 and P5-22).
/// </summary>
/// <remarks>
/// Deliberately varied, for the reason the other fakes give: axe only has an opinion about labels,
/// alternative text, and reading order once there is something on the page. The fixture therefore
/// carries a described image, an <strong>undescribed</strong> one, a decorative one, and a document —
/// which between them put every branch of the grid tile and of the picker's warning into the markup.
/// The undescribed item is the important one: it is the branch that renders a warning, and a gate
/// that only ever saw well-formed items would never look at it.
/// <para>
/// The signed URLs are made up, and that is fine here: nothing fetches them under a static render,
/// and what axe judges is whether an <c>&lt;img&gt;</c> carries an <c>alt</c> — not what it resolves
/// to. A real signer would need a key and would prove nothing this gate is about.
/// </para>
/// </remarks>
public sealed class FakeMediaClient : IMediaClient
{
    /// <summary>Identity of the item the page editor's fixture places.</summary>
    public const int PlacedId = 7;

    /// <inheritdoc />
    public Task<MediaListResult> ListAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(new MediaListResult(
            [
                Item(PlacedId, "team-photo.jpg", altText: "The team assembling a prototype"),
                Item(8, "undescribed.jpg", altText: null),
                Item(9, "divider.png", altText: null, isDecorative: true, contentType: "image/png"),
                Item(10, "prospectus.pdf", altText: null, kind: "Document", contentType: "application/pdf"),
            ],
            Total: 4,
            query.Skip,
            query.Take));
    }

    /// <inheritdoc />
    public Task<MediaDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<MediaDetail?>(Item(id, "team-photo.jpg", "The team assembling a prototype"));

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, MediaLinks>> LinksAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return Task.FromResult<IReadOnlyDictionary<int, MediaLinks>>(ids.Distinct().ToDictionary(
            id => id,
            id => new MediaLinks(
                id,
                $"/media/{id}/320x240/contain/photo.webp?v=0&s=fixture",
                $"/media/{id}/960x720/contain/photo.webp?v=0&s=fixture",
                $"/media/{id}/file/photo?v=0&s=fixture")));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaFolderNode>> FoldersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MediaFolderNode>>(
        [
            new MediaFolderNode(1, null, "Campaigns", 0, 12,
                [new MediaFolderNode(2, 1, "Spring", 0, 4, [])]),
            new MediaFolderNode(3, null, "Documents", 1, 3, []),
        ]);

    /// <inheritdoc />
    public Task<StructureClientResult<MediaFolderNode>> CreateFolderAsync(
        CreateMediaFolderRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaFolderNode>.Success(
            new MediaFolderNode(4, null, request?.Name ?? "New", 2, 0, [])));

    /// <inheritdoc />
    public Task<StructureClientResult<MediaUploadResult>> UploadAsync(
        MediaUploadContent content,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaUploadResult>.Success(
            new MediaUploadResult(Item(PlacedId, "team-photo.jpg", "The team"), Deduplicated: false, [])));

    /// <inheritdoc />
    public Task<StructureClientResult<MediaUploadResult>> ReplaceAsync(
        int id,
        MediaUploadContent content,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaUploadResult>.Success(
            new MediaUploadResult(Item(id, "team-photo.jpg", "The team"), Deduplicated: false, [])));

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDetail>> PatchAsync(
        int id,
        PatchMediaRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaDetail>.Success(
            Item(id, "team-photo.jpg", "The team")));

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDetail>> SetEditsAsync(
        int id,
        SetMediaEditsRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaDetail>.Success(
            Item(id, "team-photo.jpg", "The team")));

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDetail>> RevertEditsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaDetail>.Success(
            Item(id, "team-photo.jpg", "The team")));

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaDeleteResult>.Success(
            new MediaDeleteResult(id, WasPermanent: false, RemainingRenditions: 3)));

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDetail>> RestoreAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaDetail>.Success(
            Item(id, "team-photo.jpg", "The team")));

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDeleteResult>> PurgeAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<MediaDeleteResult>.Success(
            new MediaDeleteResult(id, WasPermanent: true, RemainingRenditions: 0)));

    /// <summary>
    /// A where-used list with something in every branch, since it is the richest markup on the
    /// item screen and the one the delete buttons are read against.
    /// </summary>
    public Task<ReferenceImpact> WhereUsedAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(FakeReusableClient.Impact);

    private static MediaDetail Item(
        int id,
        string fileName,
        string? altText,
        bool isDecorative = false,
        string kind = "Image",
        string contentType = "image/jpeg") =>
        new(
            id,
            FolderId: 1,
            FileName: $"{id:D8}.jpg",
            OriginalFileName: fileName,
            ContentType: contentType,
            Kind: kind,
            SizeBytes: 482_112,
            Width: kind == "Image" ? 2400 : null,
            Height: kind == "Image" ? 1600 : null,
            AltText: altText,
            IsDecorative: isDecorative,
            Title: "Team photograph",
            Caption: null,
            Credit: "Contoso",
            FocalPointX: null,
            FocalPointY: null,
            EditsVersion: 0,
            Edits: null,
            UploadedOn: DateTimeOffset.UnixEpoch,
            RowVersion: "AAAAAAAAB9E=");
}
