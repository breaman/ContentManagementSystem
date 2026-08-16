using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="IReusableClient"/>, over the management API.
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// Structured exactly as <see cref="HttpPageClient"/> — the same antiforgery fetch, the same
/// <c>If-Match</c> quoting, the same failure projection — because it talks to a route table that was
/// deliberately shaped like the pages' one. Kept as its own type rather than folded into a shared
/// base with the other two clients: the duplication here is a dozen lines of transport, and a base
/// class would put the three clients' lifetimes and token caches in one place where they are
/// currently independent.
/// </remarks>
public sealed class HttpReusableClient(HttpClient http) : IReusableClient
{
    private const string Base = "api/cms/v1";

    private AntiforgeryTokenResponse? _token;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReusableContentSummary>> ListAsync(
        int? folderId = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();

        if (folderId is { } folder) parameters.Add($"folderId={folder}");
        if (!string.IsNullOrWhiteSpace(search)) parameters.Add($"q={Uri.EscapeDataString(search)}");

        var path = $"{Base}/reusable{(parameters.Count > 0 ? "?" + string.Join('&', parameters) : string.Empty)}";

        return await GetAsync<List<ReusableContentSummary>>(path, cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public Task<ReusableContentDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<ReusableContentDetail>($"{Base}/reusable/{id}", cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CapturedSlot>> GetPropertiesAsync(
        int blockTypeId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetAsync<BlockTypeRevisionDetail>(
            $"{Base}/block-types/{blockTypeId}/revisions/{revision}",
            cancellationToken);

        return detail is null ? [] : CapturedSlot.Read(detail.Properties);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<BlockTypeSummary>>($"{Base}/block-types", cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableContentDetail>> CreateAsync(
        CreateReusableContentRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateReusableContentRequest, ReusableContentDetail>(
            HttpMethod.Post,
            $"{Base}/reusable",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableContentDetail>> PatchAsync(
        int id,
        PatchReusableContentRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<PatchReusableContentRequest, ReusableContentDetail>(
            HttpMethod.Patch,
            $"{Base}/reusable/{id}",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableDraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<SaveDraftRequest, ReusableDraftSaveResult>(
            HttpMethod.Put,
            $"{Base}/reusable/{id}/draft",
            request,
            cancellationToken,
            request.ExpectedRowVersion);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReusableVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<ReusableVersionSummary>>(
            $"{Base}/reusable/{id}/versions",
            cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<StructureClientResult<ReusablePublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, ReusablePublishValidation>(
            HttpMethod.Post,
            $"{Base}/reusable/{id}/validate",
            null,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusablePublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        SendAsync<PublishPageRequest, ReusablePublishResult>(
            HttpMethod.Post,
            $"{Base}/reusable/{id}/publish",
            new PublishPageRequest(acknowledgeWarnings),
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableUnpublishResult>> UnpublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        SendAsync<PublishPageRequest, ReusableUnpublishResult>(
            HttpMethod.Post,
            $"{Base}/reusable/{id}/unpublish",
            new PublishPageRequest(acknowledgeWarnings),
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, ReusableDeleteResult>(
            HttpMethod.Delete,
            $"{Base}/reusable/{id}",
            null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<ReferenceImpact> WhereUsedAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        await GetAsync<ReferenceImpact>($"{Base}/references/reusable/{id}", cancellationToken)
            ?? ReferenceImpact.None;

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);

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
    /// Matched by shape rather than through a shared interface, for the reason the page client gives:
    /// the wire contract is the record, and an interface added for a client's convenience would put a
    /// serialization-irrelevant type in everyone's way.
    /// </remarks>
    private static IReadOnlyList<ApiDiagnostic> Warnings<T>(T value) => value switch
    {
        ReusableDraftSaveResult draft => draft.Warnings,
        ReusablePublishResult published => published.Warnings,
        ReusablePublishValidation validation => validation.Warnings,
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
                "The server did not issue an antiforgery token, so no reusable content can be saved.");
}
