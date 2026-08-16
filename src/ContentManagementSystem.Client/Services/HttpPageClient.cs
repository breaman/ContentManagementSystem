using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="IPageClient"/>, over the management API.
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// Writes carry the antiforgery token, fetched once per client instance, exactly as
/// <see cref="HttpStructureClient"/> does. The one thing this client adds beyond that pattern is the
/// <c>If-Match</c> header on a draft save: the API refuses an unconditional one with <c>428</c>, and
/// a screen that had to remember to send a precondition would eventually forget.
/// </remarks>
public sealed class HttpPageClient(HttpClient http) : IPageClient
{
    private const string Base = "api/cms/v1";

    private AntiforgeryTokenResponse? _token;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
        int? parentId = null,
        int depth = 1,
        CancellationToken cancellationToken = default)
    {
        var query = parentId is null ? $"?depth={depth}" : $"?parentId={parentId}&depth={depth}";

        return await GetAsync<List<PageTreeNode>>($"{Base}/pages/tree{query}", cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public async Task<CursorPage<PageSummary>> ListAsync(
        PageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new List<string>();

        if (query.RootOnly) parameters.Add("rootOnly=true");
        if (query.ParentId is { } parentId) parameters.Add($"parentId={parentId}");
        if (query.TemplateId is { } templateId) parameters.Add($"templateId={templateId}");
        if (!string.IsNullOrWhiteSpace(query.Status)) parameters.Add($"status={Uri.EscapeDataString(query.Status)}");
        if (!string.IsNullOrWhiteSpace(query.Search)) parameters.Add($"q={Uri.EscapeDataString(query.Search)}");
        if (query.ModifiedAfter is { } after) parameters.Add($"modifiedAfter={after:O}");
        if (!string.IsNullOrWhiteSpace(query.Cursor)) parameters.Add($"cursor={Uri.EscapeDataString(query.Cursor)}");
        if (query.Limit is { } limit) parameters.Add($"limit={limit}");

        var path = $"{Base}/pages{(parameters.Count > 0 ? "?" + string.Join('&', parameters) : string.Empty)}";

        return await GetAsync<CursorPage<PageSummary>>(path, cancellationToken) ?? CursorPage<PageSummary>.Empty;
    }

    /// <inheritdoc />
    public Task<PageDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<PageDetail>($"{Base}/pages/{id}", cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CapturedSlot>> GetZonesAsync(
        int templateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetAsync<TemplateRevisionDetail>(
            $"{Base}/templates/{templateId}/revisions/{revision}",
            cancellationToken);

        return detail is null ? [] : CapturedSlot.Read(detail.Zones);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<TemplateSummary>>($"{Base}/templates", cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<StructureClientResult<PageDetail>> CreateAsync(
        CreatePageRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreatePageRequest, PageDetail>(
            HttpMethod.Post,
            $"{Base}/pages",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<DraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<SaveDraftRequest, DraftSaveResult>(
            HttpMethod.Put,
            $"{Base}/pages/{id}/draft",
            request,
            cancellationToken,
            ifMatch: request.ExpectedRowVersion);
    }

    /// <inheritdoc />
    public Task<StructureClientResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<PatchPageMetadataRequest, PageDetail>(
            HttpMethod.Patch,
            $"{Base}/pages/{id}/metadata",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<PageMoveResult>> MoveAsync(
        int id,
        MovePageRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<MovePageRequest, PageMoveResult>(
            HttpMethod.Post,
            $"{Base}/pages/{id}/move",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<PublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, PublishValidation>(
            HttpMethod.Post,
            $"{Base}/pages/{id}/validate",
            body: null,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<PublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        SendAsync<PublishPageRequest, PublishResult>(
            HttpMethod.Post,
            $"{Base}/pages/{id}/publish",
            new PublishPageRequest(acknowledgeWarnings),
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<UnpublishResult>> UnpublishAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, UnpublishResult>(
            HttpMethod.Post,
            $"{Base}/pages/{id}/unpublish",
            body: null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PageVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<PageVersionSummary>>($"{Base}/pages/{id}/versions", cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<ContentDiff?> GetDiffAsync(
        int id,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<ContentDiff>(
            $"{Base}/pages/{id}/versions/{fromVersionId}/diff/{toVersionId}",
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<DraftState>> RestoreVersionAsync(
        int id,
        int versionId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, DraftState>(
            HttpMethod.Post,
            $"{Base}/pages/{id}/versions/{versionId}/restore",
            body: null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreviewTokenSummary>> GetPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<PreviewTokenSummary>>(
            $"{Base}/preview-tokens?pageId={id}",
            cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<StructureClientResult<IssuedPreviewToken>> IssuePreviewTokenAsync(
        CreatePreviewTokenRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreatePreviewTokenRequest, IssuedPreviewToken>(
            HttpMethod.Post,
            $"{Base}/preview-tokens",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<PreviewTokenSummary>> RevokePreviewTokenAsync(
        int tokenId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, PreviewTokenSummary>(
            HttpMethod.Delete,
            $"{Base}/preview-tokens/{tokenId}",
            body: null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<StructureClientResult<int>> RevokeAllPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<object, RevokedCount>(
            HttpMethod.Delete,
            $"{Base}/preview-tokens?pageId={id}",
            body: null,
            cancellationToken);

        return result.IsSuccess
            ? StructureClientResult<int>.Success(result.Value!.Revoked, result.Warnings)
            : StructureClientResult<int>.Failure(result.Errors, result.Warnings);
    }

    /// <summary>The body of a bulk revocation, which returns a count rather than a resource.</summary>
    private sealed record RevokedCount(int Revoked);

    /// <summary>Reads a resource, treating "not there" as null rather than as a fault.</summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(path, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) return default;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task<StructureClientResult<TResult>> SendAsync<TBody, TResult>(
        HttpMethod method,
        string path,
        TBody? body,
        CancellationToken cancellationToken,
        string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null) request.Content = JsonContent.Create(body);

        var token = await TokenAsync(cancellationToken);

        request.Headers.Add(token.HeaderName, token.RequestToken);

        // Quoted, because an entity tag is a quoted string and an unquoted one is not a header the
        // server is required to understand.
        if (!string.IsNullOrEmpty(ifMatch))
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        }

        var response = await http.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<TResult>(cancellationToken);

            return value is null
                ? StructureClientResult<TResult>.Failure(
                    "client.empty-response",
                    "The server accepted the change but returned nothing to show.")
                : StructureClientResult<TResult>.Success(value, Warnings(value));
        }

        return await FailureAsync<TResult>(response, cancellationToken);
    }

    /// <summary>
    /// Pulls the warnings out of a successful response body.
    /// </summary>
    /// <remarks>
    /// A save can succeed and still have something to say — a zone whose definition was removed
    /// since the page was authored is stored <em>and</em> reported (spec section 8.5). Matched by
    /// shape rather than through a shared interface, for the reason the structure client gives: the
    /// wire contract is the record, and an interface added for a client's convenience would put a
    /// serialization-irrelevant type in everyone's way.
    /// </remarks>
    private static IReadOnlyList<ApiDiagnostic> Warnings<T>(T value) => value switch
    {
        DraftSaveResult draft => draft.Warnings,
        PublishResult published => published.Warnings,
        PublishValidation validation => validation.Warnings,
        _ => [],
    };

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
                "The server did not issue an antiforgery token, so no page change can be saved.");
}
