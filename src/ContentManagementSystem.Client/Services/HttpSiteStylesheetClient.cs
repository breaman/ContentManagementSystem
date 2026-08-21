using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Appearance;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="ISiteStylesheetClient"/>, over the management API
/// (task P10-11).
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
public sealed class HttpSiteStylesheetClient(HttpClient http) : ISiteStylesheetClient
{
    private const string Base = "api/cms/v1/appearance/stylesheet";

    private AntiforgeryTokenResponse? token;

    /// <inheritdoc />
    public Task<SiteStylesheetDetail?> GetAsync(CancellationToken cancellationToken = default) =>
        GetAsync<SiteStylesheetDetail>(Base, cancellationToken);

    /// <inheritdoc />
    public async Task<CssValidationReport?> ValidateAsync(
        string css,
        CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<ValidateSiteStylesheetRequest, CssValidationReport>(
            HttpMethod.Post,
            $"{Base}/validate",
            new ValidateSiteStylesheetRequest(css),
            rowVersion: null,
            cancellationToken);

        return result.Value;
    }

    /// <inheritdoc />
    public Task<StructureClientResult<SiteStylesheetDetail>> SaveDraftAsync(
        string css,
        string? rowVersion,
        CancellationToken cancellationToken = default) =>
        SendAsync<SaveSiteStylesheetDraftRequest, SiteStylesheetDetail>(
            HttpMethod.Put,
            $"{Base}/draft",
            new SaveSiteStylesheetDraftRequest(css),
            rowVersion,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<SiteStylesheetDetail>> PublishAsync(
        string? note,
        CancellationToken cancellationToken = default) =>
        SendAsync<PublishSiteStylesheetRequest, SiteStylesheetDetail>(
            HttpMethod.Post,
            $"{Base}/publish",
            new PublishSiteStylesheetRequest(note),
            rowVersion: null,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<SiteStylesheetDetail>> RevertAsync(
        int? revisionId,
        bool copyToDraft,
        CancellationToken cancellationToken = default) =>
        SendAsync<RevertSiteStylesheetRequest, SiteStylesheetDetail>(
            HttpMethod.Post,
            $"{Base}/revert",
            new RevertSiteStylesheetRequest(revisionId, copyToDraft),
            rowVersion: null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SiteStylesheetRevisionSummary>> GetRevisionsAsync(
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<SiteStylesheetRevisionSummary>>($"{Base}/revisions", cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<string?> GetRevisionCssAsync(
        int revisionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<string>($"{Base}/revisions/{revisionId}", cancellationToken);

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(path, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound) return default;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task<StructureClientResult<TResult>> SendAsync<TBody, TResult>(
        HttpMethod method,
        string path,
        TBody body,
        string? rowVersion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };

        var antiforgery = await TokenAsync(cancellationToken);

        request.Headers.Add(antiforgery.HeaderName, antiforgery.RequestToken);

        // The concurrency token travels as If-Match, which is what makes a lost race a 409 carrying
        // the stylesheet that won rather than a silent overwrite (spec section 11.8).
        if (!string.IsNullOrEmpty(rowVersion))
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
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

    private async Task<AntiforgeryTokenResponse> TokenAsync(CancellationToken cancellationToken) =>
        this.token ??= await http.GetFromJsonAsync<AntiforgeryTokenResponse>(
            "api/cms/v1/antiforgery-token",
            cancellationToken) ?? throw new InvalidOperationException(
                "The server did not issue an antiforgery token, so no stylesheet change can be saved.");
}
