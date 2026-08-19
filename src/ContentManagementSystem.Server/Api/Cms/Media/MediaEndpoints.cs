using System.Net;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Library;
using ContentManagementSystem.Core.Media.Upload;
using ContentManagementSystem.Server.Security;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;

namespace ContentManagementSystem.Server.Api.Cms.Media;

/// <summary>
/// <c>/api/cms/v1/media</c> — the media library's management surface (task P5-23,
/// spec section 22.1).
/// </summary>
/// <remarks>
/// Three services behind one route prefix, split by what each is responsible for rather than by what
/// the URL looks like: the upload pipeline owns every path by which bytes enter the store, the
/// library service owns metadata and the lifecycle, and the folder service owns the tree. The split
/// is what keeps the sniffing, scanning, and decode-bomb guard on exactly one code path — a
/// replacement endpoint that reached past the pipeline would be a way into the library that skips
/// every refusal in it.
/// <para>
/// <strong>Nothing here serves an image.</strong> Bytes leave through the signed rendition endpoint
/// in <c>Server/Media/</c>, which is anonymous, cached for a year, and content-type pinned. This
/// surface hands out ids and metadata; a client turns those into pictures by asking for signed URLs
/// (spec section 13.5).
/// </para>
/// </remarks>
public static class MediaEndpoints
{
    /// <summary>Route prefix every media management endpoint hangs off.</summary>
    internal const string Prefix = "/media";

    /// <summary>Form field the upload and replace endpoints read the file from.</summary>
    private const string FileField = "file";

    /// <summary>
    /// Maps the media endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapMediaEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var media = group.MapGroup(Prefix).WithTags("Media");

        // Read once, at map time, from the same options object the pipeline reads on every upload.
        var uploadOptions = ((IEndpointRouteBuilder)group).ServiceProvider
            .GetRequiredService<MediaUploadOptions>();

        var bodyLimit = new MediaBodySizeLimit(uploadOptions);

        // A part is bounded far more tightly than a whole file: the transport ceiling for the
        // chunked route is one part, not one upload, which is most of the point of having it.
        var chunkLimit = new MediaChunkSizeLimit(uploadOptions);

        media.MapPost("/", UploadAsync)
            .WithName("UploadMedia")
            .WithSummary("Uploads a file. Identical bytes return the item already in the library.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery()
            .RequireRateLimiting(CmsRateLimits.Upload)
            .WithMetadata(bodyLimit)
            .Accepts<IFormFile>("multipart/form-data");

        // Before the /{id:int} routes for the reason the folder routes are: readability. The int
        // constraint already stops "uploads" matching them.
        // Twenty a minute per user, on the three routes that begin an upload rather than on every
        // request one makes (spec section 20.6, task P9-03). A resumable upload of a 50 MB document
        // is thirteen four-megabyte parts, and counting each of those as an upload would put the
        // budget at one and a half files a minute.
        media.MapPost("/uploads", StartUploadAsync)
            .WithName("StartChunkedUpload")
            .WithSummary("Opens a resumable upload for a large file, in parts the server sizes.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery()
            .RequireRateLimiting(CmsRateLimits.Upload);

        media.MapGet("/uploads/{uploadId}", GetUploadAsync)
            .WithName("GetChunkedUpload")
            .WithSummary("Reports how much of a resumable upload has arrived, so a client can resume.")
            .RequireAuthorization(CmsPermissions.MediaUpload);

        media.MapPut("/uploads/{uploadId}/parts/{index:int}", AppendUploadAsync)
            .WithName("AppendChunkedUpload")
            .WithSummary("Sends the next part. Parts arrive in order and the response says which is next.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery()
            .WithMetadata(chunkLimit)
            .Accepts<Stream>("application/octet-stream");

        media.MapPost("/uploads/{uploadId}/complete", CompleteUploadAsync)
            .WithName("CompleteChunkedUpload")
            .WithSummary("Assembles the parts and puts them through the ordinary upload pipeline.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapDelete("/uploads/{uploadId}", AbandonUploadAsync)
            .WithName("AbandonChunkedUpload")
            .WithSummary("Abandons a resumable upload and discards everything staged for it.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapGet("/", ListAsync)
            .WithName("ListMedia")
            .WithSummary("Browses the library, filtered by folder, kind, search text, or usage.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        media.MapGet("/links", LinksAsync)
            .WithName("GetMediaLinks")
            .WithSummary("Signs thumbnail, preview, and original URLs for a batch of items.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        // Before the /{id:int} routes only for readability — the int constraint already stops
        // "folders" matching them.
        media.MapGet("/folders", ListFoldersAsync)
            .WithName("ListMediaFolders")
            .WithSummary("Reads the whole folder tree, with the item count in each folder.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        media.MapPost("/folders", CreateFolderAsync)
            .WithName("CreateMediaFolder")
            .WithSummary("Creates a folder.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapPatch("/folders/{id:int}", PatchFolderAsync)
            .WithName("PatchMediaFolder")
            .WithSummary("Renames, reorders, or moves a folder and everything beneath it.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapDelete("/folders/{id:int}", DeleteFolderAsync)
            .WithName("DeleteMediaFolder")
            .WithSummary("Deletes an empty folder. Refused while it holds anything.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapGet("/{id:int}", GetAsync)
            .WithName("GetMedia")
            .WithSummary("Reads an item's metadata and its library-scope edits.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        media.MapPatch("/{id:int}", PatchAsync)
            .WithName("PatchMedia")
            .WithSummary("Updates alt text, title, caption, credit, folder. Omitted members are left alone.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapPut("/{id:int}/edits", SetEditsAsync)
            .WithName("SetMediaEdits")
            .WithSummary("Applies library-scope rotate, flip, crop, and focal point. Bumps EditsVersion.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapPost("/{id:int}/revert", RevertEditsAsync)
            .WithName("RevertMediaEdits")
            .WithSummary("Discards the library-scope edits, returning the item to the uploaded bytes.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapPost("/{id:int}/replace", ReplaceAsync)
            .WithName("ReplaceMedia")
            .WithSummary("Puts new bytes behind the item, keeping its id and every page pointing at it.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery()
            .WithMetadata(bodyLimit)
            .Accepts<IFormFile>("multipart/form-data")
            .RequireRateLimiting(CmsRateLimits.Upload);

        media.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteMedia")
            .WithSummary("Moves the item to the recycle bin.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapPost("/{id:int}/restore", RestoreAsync)
            .WithName("RestoreMedia")
            .WithSummary("Brings an item back out of the recycle bin.")
            .RequireAuthorization(CmsPermissions.MediaUpload)
            .RequireCmsAntiforgery();

        media.MapDelete("/{id:int}/permanent", PurgeAsync)
            .WithName("PurgeMedia")
            .WithSummary("Deletes the item and its bytes for good. Refused while any content shows it.")
            .RequireAuthorization(CmsPermissions.MediaDelete)
            .RequireCmsAntiforgery();

        media.MapGet("/{id:int}/references", ReferencesAsync)
            .WithName("GetMediaWhereUsed")
            .WithSummary("Lists the pages and reusable items whose content shows this file.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        return group;
    }

    /// <summary>
    /// Accepts a multipart upload.
    /// </summary>
    /// <remarks>
    /// Takes <see cref="HttpRequest"/> rather than an <c>IFormFile</c> parameter, for the reason the
    /// redirect CSV import does: what arrives is a file plus a handful of text fields, and reading
    /// the form here is what lets the two body limits be set from configuration before a single byte
    /// is buffered.
    /// <para>
    /// A deduplicated upload answers <c>200</c> with the existing item rather than <c>201</c>. The
    /// distinction is the honest one — nothing was created — and the <c>deduplicated</c> flag in the
    /// body is what lets the client say "this is the same photo you uploaded in March" instead of
    /// silently filing a second copy (spec section 13.1).
    /// </para>
    /// </remarks>
    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        IMediaUploadService uploads,
        MediaUploadOptions options,
        CancellationToken cancellationToken)
    {
        var form = await MediaUploadForm.ReadAsync(request, options, cancellationToken);

        if (form.Refusal is { } refusal) return refusal;

        await using var content = form.Content!;

        var result = await uploads.UploadAsync(
            form.ToRequest(content),
            cancellationToken);

        return result.ToHttpResult(upload => upload.Deduplicated
            ? Results.Ok(upload)
            : Results.Created($"{CmsApiEndpoints.BasePath}{Prefix}/{upload.Item.Id}", upload));
    }

    /// <remarks>
    /// The same screening as an upload, against an item that already exists. Metadata fields left
    /// out of the form keep what the item has, so replacing a photograph does not silently blank its
    /// credit line.
    /// </remarks>
    private static async Task<IResult> ReplaceAsync(
        int id,
        HttpRequest request,
        IMediaUploadService uploads,
        MediaUploadOptions options,
        CancellationToken cancellationToken)
    {
        var form = await MediaUploadForm.ReadAsync(request, options, cancellationToken);

        if (form.Refusal is { } refusal) return refusal;

        await using var content = form.Content!;

        var result = await uploads.ReplaceAsync(id, form.ToRequest(content), cancellationToken);

        return result.ToHttpResult(Results.Ok);
    }

    /// <remarks>
    /// The session is created from a declaration rather than from bytes, so the extension allowlist
    /// and the size ceiling are applied before the client starts transferring — the alternative is
    /// an editor watching a progress bar reach the end of a file that was never going to be accepted
    /// (task P5-08).
    /// </remarks>
    private static async Task<IResult> StartUploadAsync(
        StartChunkedUploadRequest request,
        IChunkedUploadService uploads,
        CancellationToken cancellationToken) =>
        (await uploads.StartAsync(request, cancellationToken))
        .ToHttpResult(session => Results.Created(
            $"{CmsApiEndpoints.BasePath}{Prefix}/uploads/{session.UploadId}",
            session));

    private static async Task<IResult> GetUploadAsync(
        string uploadId,
        IChunkedUploadService uploads,
        CancellationToken cancellationToken) =>
        (await uploads.GetAsync(uploadId, cancellationToken)).ToHttpResult(Results.Ok);

    /// <remarks>
    /// Reads the raw request body rather than a form: a part is a slice of a file and has no name,
    /// no type, and no other fields travelling with it, so multipart framing would be envelope
    /// around nothing. The service bounds what it reads by the part size it chose, and the endpoint
    /// carries the matching transport ceiling so Kestrel refuses an oversized body first.
    /// </remarks>
    private static async Task<IResult> AppendUploadAsync(
        string uploadId,
        int index,
        HttpRequest request,
        IChunkedUploadService uploads,
        CancellationToken cancellationToken) =>
        (await uploads.AppendAsync(uploadId, index, request.Body, cancellationToken))
        .ToHttpResult(Results.Ok);

    /// <remarks>
    /// Answers exactly what the single-request upload answers, deduplication included, because it is
    /// the same pipeline reached by a different transport. A client can therefore share its
    /// success handling between the two routes.
    /// </remarks>
    private static async Task<IResult> CompleteUploadAsync(
        string uploadId,
        IChunkedUploadService uploads,
        CancellationToken cancellationToken) =>
        (await uploads.CompleteAsync(uploadId, cancellationToken)).ToHttpResult(upload => upload.Deduplicated
            ? Results.Ok(upload)
            : Results.Created($"{CmsApiEndpoints.BasePath}{Prefix}/{upload.Item.Id}", upload));

    private static async Task<IResult> AbandonUploadAsync(
        string uploadId,
        IChunkedUploadService uploads,
        CancellationToken cancellationToken) =>
        (await uploads.AbandonAsync(uploadId, cancellationToken))
        .ToHttpResult(discarded => Results.Ok(new { uploadId = discarded }));

    private static async Task<IResult> ListAsync(
        IMediaLibraryService library,
        CancellationToken cancellationToken,
        int? folderId = null,
        bool includeDescendants = false,
        string? kind = null,
        string? q = null,
        bool unusedOnly = false,
        bool deletedOnly = false,
        int skip = 0,
        int? take = null) =>
        (await library.ListAsync(
            new MediaQuery
            {
                FolderId = folderId,
                IncludeDescendants = includeDescendants,
                Kind = kind,
                Search = q,
                UnusedOnly = unusedOnly,
                DeletedOnly = deletedOnly,
                Skip = skip,
                Take = take ?? MediaQuery.DefaultTake,
            },
            cancellationToken))
        .ToHttpResult(Results.Ok);

    /// <remarks>
    /// Stamps the row's <c>rowversion</c> as an <c>ETag</c>, which a later metadata or edits write
    /// echoes back as <c>If-Match</c>.
    /// </remarks>
    private static async Task<IResult> GetAsync(
        int id,
        HttpContext httpContext,
        IMediaLibraryService library,
        CancellationToken cancellationToken) =>
        (await library.GetAsync(id, cancellationToken)).ToHttpResult(item =>
        {
            ETags.Stamp(httpContext, item.RowVersion);

            return Results.Ok(item);
        });

    /// <remarks>
    /// The precondition is honoured but not required, as on the page metadata patch: a patch names
    /// the members it changes, so two editors changing different fields merge rather than collide.
    /// </remarks>
    private static async Task<IResult> PatchAsync(
        int id,
        PatchMediaRequest request,
        HttpContext httpContext,
        IMediaLibraryService library,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await library.PatchAsync(
            id,
            request with { ExpectedRowVersion = ETags.IfMatch(httpContext) ?? request.ExpectedRowVersion },
            cancellationToken);

        return result.ToHttpResult(item =>
        {
            ETags.Stamp(httpContext, item.RowVersion);

            return Results.Ok(item);
        });
    }

    /// <remarks>
    /// Unlike the metadata patch, this one is a whole-document replacement, so a lost race is a lost
    /// crop rather than a merge. The precondition is honoured when sent and not insisted on: the
    /// image editor is a single-item screen an editor opens deliberately, and an <c>If-Match</c> the
    /// client already has costs it nothing to send.
    /// </remarks>
    private static async Task<IResult> SetEditsAsync(
        int id,
        SetMediaEditsRequest request,
        HttpContext httpContext,
        IMediaLibraryService library,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await library.SetEditsAsync(
            id,
            request with { ExpectedRowVersion = ETags.IfMatch(httpContext) ?? request.ExpectedRowVersion },
            cancellationToken);

        return result.ToHttpResult(item =>
        {
            ETags.Stamp(httpContext, item.RowVersion);

            return Results.Ok(item);
        });
    }

    private static async Task<IResult> RevertEditsAsync(
        int id,
        HttpContext httpContext,
        IMediaLibraryService library,
        CancellationToken cancellationToken) =>
        (await library.RevertEditsAsync(id, cancellationToken)).ToHttpResult(item =>
        {
            ETags.Stamp(httpContext, item.RowVersion);

            return Results.Ok(item);
        });

    private static async Task<IResult> DeleteAsync(
        int id,
        IMediaLibraryService library,
        CancellationToken cancellationToken) =>
        (await library.DeleteAsync(id, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> RestoreAsync(
        int id,
        IMediaLibraryService library,
        CancellationToken cancellationToken) =>
        (await library.RestoreAsync(id, cancellationToken)).ToHttpResult(Results.Ok);

    /// <remarks>
    /// Answers <c>409</c> with the where-used sentence while anything still shows the file
    /// (task P5-24, spec section 13.8). The client pairs that with <c>GET /media/{id}/references</c>
    /// to show the list — the refusal carries the reason, the reference endpoint carries the rows.
    /// </remarks>
    private static async Task<IResult> PurgeAsync(
        int id,
        IMediaLibraryService library,
        CancellationToken cancellationToken) =>
        (await library.PurgeAsync(id, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> ReferencesAsync(
        int id,
        IMediaLibraryService library,
        CancellationToken cancellationToken) =>
        (await library.WhereUsedAsync(id, cancellationToken)).ToHttpResult(Results.Ok);

    /// <summary>
    /// Signs the URLs a batch of items can be shown at.
    /// </summary>
    /// <remarks>
    /// Batched, and separate from the list endpoint, for two different reasons. Batched because a
    /// grid of fifty thumbnails must not be fifty requests. Separate because the signing key is a
    /// delivery concern and the library service does not have one — see <see cref="MediaLinks"/>.
    /// <para>
    /// The ids are read back through the library service rather than signed blindly, so an item that
    /// does not exist, or is in the recycle bin the caller is not looking at, simply produces no
    /// entry. Signing whatever id arrived would turn this into an oracle for which ids exist.
    /// </para>
    /// </remarks>
    private static async Task<IResult> LinksAsync(
        IMediaLibraryService library,
        IMediaUrlSigner signer,
        CancellationToken cancellationToken,
        string? ids = null)
    {
        var wanted = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(candidate => int.TryParse(candidate, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(MediaQuery.MaxTake)
            .ToList();

        var links = new List<MediaLinks>(wanted.Count);

        foreach (var id in wanted)
        {
            var item = await library.GetAsync(id, cancellationToken);

            if (item.Value is { } detail) links.Add(MediaLinkFactory.For(detail, signer));
        }

        return Results.Ok(links);
    }

    private static async Task<IResult> ListFoldersAsync(
        IMediaFolderService folders,
        CancellationToken cancellationToken) =>
        (await folders.ListAsync(cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> CreateFolderAsync(
        CreateMediaFolderRequest request,
        IMediaFolderService folders,
        CancellationToken cancellationToken) =>
        (await folders.CreateAsync(request, cancellationToken))
        .ToHttpResult(folder => Results.Created(
            $"{CmsApiEndpoints.BasePath}{Prefix}/folders/{folder.Id}",
            folder));

    private static async Task<IResult> PatchFolderAsync(
        int id,
        PatchMediaFolderRequest request,
        IMediaFolderService folders,
        CancellationToken cancellationToken) =>
        (await folders.PatchAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> DeleteFolderAsync(
        int id,
        IMediaFolderService folders,
        CancellationToken cancellationToken) =>
        (await folders.DeleteAsync(id, cancellationToken))
        .ToHttpResult(deleted => Results.Ok(new { id = deleted }));

    /// <summary>
    /// The largest request body the upload routes accept, as endpoint metadata (task P5-05,
    /// spec section 13.3 step 1).
    /// </summary>
    /// <param name="options">The deployment's upload limits.</param>
    /// <remarks>
    /// Metadata rather than a line in the handler, because Kestrel applies this before any
    /// middleware or filter touches the body — including the antiforgery filter, which reads the
    /// form when a client sends its token as a form field rather than a header. A ceiling set inside
    /// the handler arrives after that read, which is precisely too late.
    /// <para>
    /// The larger of the two per-kind ceilings, plus room for the multipart envelope: which kind
    /// applies is not known until the file's extension has been parsed out of the body, and the
    /// pipeline applies the exact one once it is.
    /// </para>
    /// </remarks>
    private sealed class MediaBodySizeLimit(MediaUploadOptions options) : IRequestSizeLimitMetadata
    {
        /// <inheritdoc />
        public long? MaxRequestBodySize { get; } =
            Math.Max(options.MaxImageBytes, options.MaxDocumentBytes) + MultipartOverheadBytes;
    }

    /// <summary>Room for the multipart envelope on top of the file itself.</summary>
    private const long MultipartOverheadBytes = 64 * 1024;

    /// <summary>
    /// The largest body one part of a resumable upload may have (task P5-08).
    /// </summary>
    /// <param name="options">The deployment's upload limits.</param>
    /// <remarks>
    /// The whole reason the chunked route is safer than the single-request one: the transport
    /// ceiling here is a <em>part</em>, so a fifty-megabyte video never puts fifty megabytes into
    /// one request or one buffer. The service applies the same bound again to the bytes it reads, so
    /// a caller that is not an HTTP request faces it too.
    /// <para>
    /// A small allowance on top of the configured part size, because a part that exactly fills it is
    /// legitimate and a ceiling equal to the payload leaves no room for framing.
    /// </para>
    /// </remarks>
    private sealed class MediaChunkSizeLimit(MediaUploadOptions options) : IRequestSizeLimitMetadata
    {
        /// <inheritdoc />
        public long? MaxRequestBodySize { get; } = options.ChunkBytes + (64L * 1024);
    }

    /// <summary>
    /// The multipart body an upload or a replacement arrives in, once it has been read and bounded.
    /// </summary>
    /// <remarks>
    /// The HTTP-layer half of pipeline step 1 (task P5-05). Two separate limits, and both are
    /// needed: <see cref="IHttpMaxRequestBodySizeFeature"/> caps the request Kestrel will read at
    /// all, and <c>FormOptions.MultipartBodyLengthLimit</c> caps what the form reader will buffer
    /// out of it. Without the first, a client can stream gigabytes before anything objects; without
    /// the second, the multipart reader is happy to hold a request that Kestrel was happy to accept.
    /// The service enforces its own ceiling again on the bytes that survive, so a caller that is not
    /// an HTTP request — the CLI, a test — faces the same limit.
    /// </remarks>
    private sealed record MediaUploadForm
    {
        /// <summary>The buffered file, or null when <see cref="Refusal"/> is set.</summary>
        public Stream? Content { get; init; }

        /// <summary>The response to return instead of proceeding, or null when the form was usable.</summary>
        public IResult? Refusal { get; init; }

        private string FileName { get; init; } = string.Empty;

        private int? FolderId { get; init; }

        private string? AltText { get; init; }

        private bool IsDecorative { get; init; }

        private string? Title { get; init; }

        private string? Caption { get; init; }

        private string? Credit { get; init; }

        /// <summary>Reads and bounds the multipart body.</summary>
        /// <param name="request">The incoming request.</param>
        /// <param name="options">The deployment's upload limits.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The parsed form, or the refusal to answer with.</returns>
        public static async Task<MediaUploadForm> ReadAsync(
            HttpRequest request,
            MediaUploadOptions options,
            CancellationToken cancellationToken)
        {
            // The transport ceiling is already in force — MediaBodySizeLimit put it on the endpoint,
            // so Kestrel refused anything larger before this method was reached. What is set here is
            // the form reader's own limit, which bounds what gets buffered out of a request the
            // transport was willing to accept.
            var ceiling = Math.Max(options.MaxImageBytes, options.MaxDocumentBytes);

            request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                MultipartBodyLengthLimit = ceiling,
                // Everything but the file is a short text field; a body claiming otherwise is not an
                // upload this endpoint has any use for.
                ValueLengthLimit = FieldLengths.Caption * 4,
                MultipartHeadersLengthLimit = 8 * 1024,
            }));

            if (!request.HasFormContentType)
            {
                return Refuse(
                    HttpStatusCode.UnsupportedMediaType,
                    MediaCodes.FileMissing,
                    "Send the file as multipart/form-data with the file in a 'file' part.");
            }

            IFormCollection form;

            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is BadHttpRequestException or InvalidDataException)
            {
                // What both limits above look like when they fire. Reported as the pipeline's own
                // "too large" rather than as a transport error, so a client switches on one code
                // whichever layer caught it.
                return Refuse(
                    HttpStatusCode.RequestEntityTooLarge,
                    MediaCodes.TooLarge,
                    $"That upload is larger than the {ceiling / (1024 * 1024)} MB limit.");
            }

            var file = form.Files.GetFile(FileField) ?? form.Files.FirstOrDefault();

            if (file is null || file.Length == 0)
            {
                return Refuse(
                    HttpStatusCode.UnprocessableEntity,
                    MediaCodes.FileMissing,
                    $"No file was supplied. Put it in a '{FileField}' part of the form.");
            }

            // Buffered to memory, and bounded by the two limits above rather than by trust: the
            // pipeline reads the bytes several times — to sniff, to scan, to hash, to store — and a
            // forward-only request stream cannot be read twice.
            var buffer = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));

            await using (var source = file.OpenReadStream())
            {
                await source.CopyToAsync(buffer, cancellationToken);
            }

            buffer.Position = 0;

            return new MediaUploadForm
            {
                Content = buffer,
                FileName = file.FileName,
                FolderId = Int(form, "folderId"),
                AltText = Text(form, "altText"),
                IsDecorative = Bool(form, "isDecorative"),
                Title = Text(form, "title"),
                Caption = Text(form, "caption"),
                Credit = Text(form, "credit"),
            };
        }

        /// <summary>Projects the form into the pipeline's request.</summary>
        /// <param name="content">The buffered bytes.</param>
        /// <returns>The upload request.</returns>
        public MediaUploadRequest ToRequest(Stream content) => new(
            content,
            FileName,
            FolderId,
            AltText,
            IsDecorative,
            Title,
            Caption,
            Credit);

        private static MediaUploadForm Refuse(HttpStatusCode status, string code, string message) =>
            new()
            {
                Refusal = CmsProblems.Problem(
                    status,
                    "media-upload",
                    "Upload refused",
                    ValidationResult.Error(code, message)),
            };

        private static string? Text(IFormCollection form, string key) =>
            form.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString() : null;

        private static int? Int(IFormCollection form, string key) =>
            int.TryParse(Text(form, key), out var value) ? value : null;

        private static bool Bool(IFormCollection form, string key) =>
            bool.TryParse(Text(form, key), out var value) && value;
    }
}
