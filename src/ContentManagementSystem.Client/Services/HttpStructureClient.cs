using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="IStructureClient"/>, over the management API.
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// Every write carries an antiforgery token, fetched once per client instance and cached. The API is
/// cookie-authenticated, so without it every write is forgeable from any page a signed-in developer
/// visits; the header name comes from the server rather than being hard-coded, so changing it is a
/// configuration change and not a coordinated deployment.
/// </remarks>
public sealed class HttpStructureClient(HttpClient http) : IStructureClient
{
    private const string Base = "api/cms/v1";

    private AntiforgeryTokenResponse? _token;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<TemplateSummary>>($"{Base}/templates", cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<TemplateDetail?> GetTemplateAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<TemplateDetail>($"{Base}/templates/{id}", cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<TemplateDetail>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<CreateTemplateRequest, TemplateDetail>($"{Base}/templates", request, cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ZoneSaveResult>> CreateZoneAsync(
        int templateId,
        CreateZoneRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<CreateZoneRequest, ZoneSaveResult>(
            $"{Base}/templates/{templateId}/zones",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ZoneSaveResult>> UpdateZoneAsync(
        int templateId,
        int zoneId,
        UpdateZoneRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<UpdateZoneRequest, ZoneSaveResult>(
            HttpMethod.Put,
            $"{Base}/templates/{templateId}/zones/{zoneId}",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ZoneRemovalResult>> DeleteZoneAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, ZoneRemovalResult>(
            HttpMethod.Delete,
            $"{Base}/templates/{templateId}/zones/{zoneId}",
            body: null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<BlockTypeSummary>>($"{Base}/block-types", cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<BlockTypeDetail?> GetBlockTypeAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<BlockTypeDetail>($"{Base}/block-types/{id}", cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<BlockTypeDetail>> CreateBlockTypeAsync(
        CreateBlockTypeRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<CreateBlockTypeRequest, BlockTypeDetail>($"{Base}/block-types", request, cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<PropertySaveResult>> CreatePropertyAsync(
        int blockTypeId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<CreatePropertyRequest, PropertySaveResult>(
            $"{Base}/block-types/{blockTypeId}/properties",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<PropertySaveResult>> UpdatePropertyAsync(
        int blockTypeId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<UpdatePropertyRequest, PropertySaveResult>(
            HttpMethod.Put,
            $"{Base}/block-types/{blockTypeId}/properties/{propertyId}",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<PropertyRemovalResult>> DeletePropertyAsync(
        int blockTypeId,
        int propertyId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, PropertyRemovalResult>(
            HttpMethod.Delete,
            $"{Base}/block-types/{blockTypeId}/properties/{propertyId}",
            body: null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FieldTypeDescriptor>> GetFieldTypesAsync(
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<FieldTypeDescriptor>>($"{Base}/field-types", cancellationToken) ?? [];

    /// <summary>
    /// Reads a resource, treating "not there" as null rather than as a fault.
    /// </summary>
    /// <remarks>
    /// A 404 is an ordinary answer to a screen bookmarked against a template someone deleted, and it
    /// belongs in the empty state rather than in an error boundary.
    /// </remarks>
    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(path, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound) return default;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private Task<StructureClientResult<TResult>> PostAsync<TBody, TResult>(
        string path,
        TBody body,
        CancellationToken cancellationToken) =>
        SendAsync<TBody, TResult>(HttpMethod.Post, path, body, cancellationToken);

    private async Task<StructureClientResult<TResult>> SendAsync<TBody, TResult>(
        HttpMethod method,
        string path,
        TBody? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null) request.Content = JsonContent.Create(body);

        var token = await TokenAsync(cancellationToken);

        request.Headers.Add(token.HeaderName, token.RequestToken);

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
    /// A save can succeed and still have something to say — a configuration setting whose phase has
    /// not shipped is stored <em>and</em> reported (spec section 7.2). The two results that carry
    /// them are matched by shape rather than by a shared interface, because the wire contract is the
    /// record and adding an interface to it for the client's convenience would put a
    /// serialization-irrelevant type in everyone's way.
    /// </remarks>
    private static IReadOnlyList<ApiDiagnostic> Warnings<T>(T value) => value switch
    {
        ZoneSaveResult zone => zone.Warnings,
        PropertySaveResult property => property.Warnings,
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
            // A refusal that is not a problem document at all — a proxy's HTML error page, most
            // likely. Reporting the status beats surfacing a parse failure the developer cannot act
            // on.
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
                "The server did not issue an antiforgery token, so no structural change can be saved.");
}
