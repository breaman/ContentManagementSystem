using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="IMediaClient"/>, over the management API (tasks P5-08 and
/// P5-22).
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// Structured like the other three HTTP clients — the same antiforgery fetch, the same failure
/// projection — with one addition they do not have: <see cref="UploadAsync"/> chooses between a
/// single request and a resumable one by the file's size.
/// <para>
/// <strong>The threshold is about failure, not speed.</strong> A single request that dies at 90% has
/// to start again from zero, which on a video over a hotel connection is the difference between an
/// upload that eventually succeeds and one that never does. Above the threshold the file goes in
/// parts, each retried independently, and the server reports where it got to.
/// </para>
/// </remarks>
public sealed class HttpMediaClient(HttpClient http) : IMediaClient
{
    private const string Base = "api/cms/v1";

    private const string Media = $"{Base}/media";

    /// <summary>
    /// Largest file sent in a single request.
    /// </summary>
    /// <remarks>
    /// 8 MB — comfortably above every photograph an editor uploads, so the ordinary case stays one
    /// round trip, and below the size at which a failed upload is genuinely expensive to repeat.
    /// </remarks>
    private const long ChunkedUploadThreshold = 8L * 1024 * 1024;

    private AntiforgeryTokenResponse? _token;

    /// <inheritdoc />
    public async Task<MediaListResult> ListAsync(
        MediaQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new List<string>();

        if (query.FolderId is { } folder) parameters.Add($"folderId={folder}");
        if (query.IncludeDescendants) parameters.Add("includeDescendants=true");
        if (!string.IsNullOrWhiteSpace(query.Kind)) parameters.Add($"kind={Uri.EscapeDataString(query.Kind)}");
        if (!string.IsNullOrWhiteSpace(query.Search)) parameters.Add($"q={Uri.EscapeDataString(query.Search)}");
        if (query.UnusedOnly) parameters.Add("unusedOnly=true");
        if (query.DeletedOnly) parameters.Add("deletedOnly=true");
        if (query.Skip > 0) parameters.Add($"skip={query.Skip}");

        parameters.Add($"take={query.Take}");

        var path = $"{Media}?{string.Join('&', parameters)}";

        return await GetAsync<MediaListResult>(path, cancellationToken)
            ?? new MediaListResult([], 0, query.Skip, query.Take);
    }

    /// <inheritdoc />
    public Task<MediaDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<MediaDetail>($"{Media}/{id}", cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, MediaLinks>> LinksAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var wanted = ids.Distinct().ToList();

        if (wanted.Count == 0) return new Dictionary<int, MediaLinks>();

        var links = await GetAsync<List<MediaLinks>>(
            $"{Media}/links?ids={string.Join(',', wanted)}",
            cancellationToken);

        return links?.ToDictionary(link => link.MediaItemId) ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaFolderNode>> FoldersAsync(
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<MediaFolderNode>>($"{Media}/folders", cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<StructureClientResult<MediaFolderNode>> CreateFolderAsync(
        CreateMediaFolderRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateMediaFolderRequest, MediaFolderNode>(
            HttpMethod.Post,
            $"{Media}/folders",
            request,
            cancellationToken);

    /// <inheritdoc />
    public async Task<StructureClientResult<MediaUploadResult>> UploadAsync(
        MediaUploadContent content,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.SizeBytes > ChunkedUploadThreshold)
        {
            return await ChunkedUploadAsync(content, progress, cancellationToken);
        }

        var result = await PostFormAsync(Media, content, cancellationToken);

        progress?.Report(1);

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always a single request, whatever the size. A replacement is an editor deliberately swapping
    /// one file for another on a screen they are watching, and the resumable path would add a
    /// session whose only purpose would be to survive an interruption that ends the screen anyway.
    /// </remarks>
    public Task<StructureClientResult<MediaUploadResult>> ReplaceAsync(
        int id,
        MediaUploadContent content,
        CancellationToken cancellationToken = default) =>
        PostFormAsync($"{Media}/{id}/replace", content, cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDetail>> PatchAsync(
        int id,
        PatchMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<PatchMediaRequest, MediaDetail>(
            HttpMethod.Patch,
            $"{Media}/{id}",
            request,
            cancellationToken,
            request.ExpectedRowVersion);
    }

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDetail>> SetEditsAsync(
        int id,
        SetMediaEditsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<SetMediaEditsRequest, MediaDetail>(
            HttpMethod.Put,
            $"{Media}/{id}/edits",
            request,
            cancellationToken,
            request.ExpectedRowVersion);
    }

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDetail>> RevertEditsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, MediaDetail>(HttpMethod.Post, $"{Media}/{id}/revert", null, cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, MediaDeleteResult>(HttpMethod.Delete, $"{Media}/{id}", null, cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDetail>> RestoreAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, MediaDetail>(HttpMethod.Post, $"{Media}/{id}/restore", null, cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<MediaDeleteResult>> PurgeAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, MediaDeleteResult>(
            HttpMethod.Delete,
            $"{Media}/{id}/permanent",
            null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<ReferenceImpact> WhereUsedAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        await GetAsync<ReferenceImpact>($"{Media}/{id}/references", cancellationToken)
            ?? ReferenceImpact.None;

    /// <summary>
    /// Sends a large file as a sequence of parts, reporting progress as the server acknowledges each.
    /// </summary>
    /// <param name="content">The file and its metadata.</param>
    /// <param name="progress">Receives the fraction the server has confirmed it holds.</param>
    /// <param name="cancellationToken">Token observed while uploading.</param>
    /// <remarks>
    /// Progress is reported from the <em>server's</em> count of received bytes, not from how much
    /// this method has written. The two differ exactly when it matters — a part that was written to
    /// the socket and lost is progress the client believes in and the server does not — and a bar
    /// that only ever moves forward on confirmation is the one that can be trusted to reach the end.
    /// <para>
    /// A part that fails is not retried here. The session survives, so the correct recovery is for
    /// the editor to try the upload again; a resumed session picks up from the index the server
    /// reports rather than from the beginning. Automatic retry belongs with the upload queue in
    /// Phase 6, where there is somewhere to show it.
    /// </para>
    /// </remarks>
    private async Task<StructureClientResult<MediaUploadResult>> ChunkedUploadAsync(
        MediaUploadContent content,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var opened = await SendAsync<StartChunkedUploadRequest, ChunkedUploadSession>(
            HttpMethod.Post,
            $"{Media}/uploads",
            new StartChunkedUploadRequest(
                content.FileName,
                content.SizeBytes,
                content.FolderId,
                content.AltText,
                content.IsDecorative,
                content.Title,
                content.Caption,
                content.Credit),
            cancellationToken);

        if (!opened.IsSuccess)
        {
            return StructureClientResult<MediaUploadResult>.Failure(opened.Errors, opened.Warnings);
        }

        var session = opened.Value!;
        var buffer = new byte[session.ChunkSize];
        var index = 0;

        while (true)
        {
            var read = await ReadFullyAsync(content.Content, buffer, cancellationToken);

            if (read == 0) break;

            using var part = new ByteArrayContent(buffer, 0, read);

            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var appended = await SendContentAsync<ChunkedUploadSession>(
                HttpMethod.Put,
                $"{Media}/uploads/{session.UploadId}/parts/{index}",
                part,
                cancellationToken);

            if (!appended.IsSuccess)
            {
                return StructureClientResult<MediaUploadResult>.Failure(appended.Errors, appended.Warnings);
            }

            session = appended.Value!;
            index++;

            progress?.Report(session.TotalBytes <= 0
                ? 1
                : Math.Min(1, (double)session.ReceivedBytes / session.TotalBytes));

            if (session.IsComplete) break;
        }

        return await SendAsync<object, MediaUploadResult>(
            HttpMethod.Post,
            $"{Media}/uploads/{session.UploadId}/complete",
            null,
            cancellationToken);
    }

    /// <summary>
    /// Fills a buffer, or returns what is left at the end of the stream.
    /// </summary>
    /// <remarks>
    /// A browser file stream returns short reads freely, and a part built from one of those would be
    /// smaller than the server's chunk size — which the server accepts, but which turns a ten-part
    /// upload into a hundred-part one. Filling the buffer keeps the part count the server planned.
    /// </remarks>
    private static async Task<int> ReadFullyAsync(
        Stream source,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(total), cancellationToken);

            if (read == 0) break;

            total += read;
        }

        return total;
    }

    /// <summary>Posts a file and its metadata as the multipart form the upload endpoints read.</summary>
    private async Task<StructureClientResult<MediaUploadResult>> PostFormAsync(
        string path,
        MediaUploadContent content,
        CancellationToken cancellationToken)
    {
        using var body = new MultipartFormDataContent
        {
            { new StreamContent(content.Content), "file", content.FileName },
        };

        if (content.FolderId is { } folder) body.Add(new StringContent(folder.ToString()), "folderId");
        if (content.AltText is { Length: > 0 } alt) body.Add(new StringContent(alt), "altText");
        if (content.IsDecorative) body.Add(new StringContent("true"), "isDecorative");
        if (content.Title is { Length: > 0 } title) body.Add(new StringContent(title), "title");
        if (content.Caption is { Length: > 0 } caption) body.Add(new StringContent(caption), "caption");
        if (content.Credit is { Length: > 0 } credit) body.Add(new StringContent(credit), "credit");

        return await SendContentAsync<MediaUploadResult>(HttpMethod.Post, path, body, cancellationToken);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) return default;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private Task<StructureClientResult<TResult>> SendAsync<TBody, TResult>(
        HttpMethod method,
        string path,
        TBody? body,
        CancellationToken cancellationToken,
        string? ifMatch = null) =>
        SendContentAsync<TResult>(
            method,
            path,
            body is null ? null : JsonContent.Create(body),
            cancellationToken,
            ifMatch);

    private async Task<StructureClientResult<TResult>> SendContentAsync<TResult>(
        HttpMethod method,
        string path,
        HttpContent? body,
        CancellationToken cancellationToken,
        string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body };

        var token = await TokenAsync(cancellationToken);

        request.Headers.Add(token.HeaderName, token.RequestToken);

        // Quoted, because an entity tag is a quoted string and an unquoted one is not a header the
        // server is required to understand.
        if (!string.IsNullOrEmpty(ifMatch))
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        }

        var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode) return await FailureAsync<TResult>(response, cancellationToken);

        var value = await response.Content.ReadFromJsonAsync<TResult>(cancellationToken);

        return value is null
            ? StructureClientResult<TResult>.Failure(
                "client.empty-response",
                "The server accepted the change but returned nothing to show.")
            : StructureClientResult<TResult>.Success(value);
    }

    private static async Task<StructureClientResult<T>> FailureAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);

            if (problem?.Errors is { Count: > 0 })
            {
                return StructureClientResult<T>.Failure(problem.Errors, problem.Warnings);
            }

            return StructureClientResult<T>.Failure(
                $"http.{(int)response.StatusCode}",
                problem?.Detail ?? response.ReasonPhrase ?? "The request was refused.");
        }
        catch (Exception exception) when (exception is HttpRequestException or NotSupportedException
            or System.Text.Json.JsonException)
        {
            return StructureClientResult<T>.Failure(
                $"http.{(int)response.StatusCode}",
                response.ReasonPhrase ?? "The request was refused.");
        }
    }

    /// <summary>Fetches the antiforgery pair once and reuses it for the client's lifetime.</summary>
    private async Task<AntiforgeryTokenResponse> TokenAsync(CancellationToken cancellationToken) =>
        _token ??= await http.GetFromJsonAsync<AntiforgeryTokenResponse>(
            $"{Base}/antiforgery-token",
            cancellationToken) ?? throw new InvalidOperationException(
                "The server did not issue an antiforgery token, so no media can be uploaded.");
}
