using System.Globalization;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="ISearchClient"/>, over the management API (tasks P8-19, P8-20).
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// The query is assembled into a query string rather than posted, which is what makes a search a URL
/// an editor can keep. Reads carry no antiforgery token — they change nothing — and the two tag
/// writes do, for the reason every other write here does.
/// </remarks>
public sealed class HttpSearchClient(HttpClient http) : ISearchClient
{
    private const string SearchBase = "api/cms/v1/search";
    private const string TagsBase = "api/cms/v1/tags";

    private AntiforgeryTokenResponse? _token;

    /// <inheritdoc />
    public async Task<SearchResults> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var response = await http.GetFromJsonAsync<SearchResults>(
            $"{SearchBase}{QueryString(query)}",
            cancellationToken);

        return response ?? new SearchResults([], 0, FullText: false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagSummary>> GetTagsAsync(
        CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<TagSummary>>(TagsBase, cancellationToken) ?? [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagSummary>> SuggestTagsAsync(
        string? prefix,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<TagSummary>>(
            $"{TagsBase}/suggest?prefix={Uri.EscapeDataString(prefix ?? string.Empty)}&limit={limit}",
            cancellationToken) ?? [];

    /// <inheritdoc />
    public async Task<StructureClientResult<RenameTagResult>> RenameTagAsync(
        int id,
        RenameTagRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"{TagsBase}/{id}")
        {
            Content = JsonContent.Create(request),
        };

        var token = await TokenAsync(cancellationToken);

        message.Headers.Add(token.HeaderName, token.RequestToken);

        var response = await http.SendAsync(message, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return await FailureAsync<RenameTagResult>(response, cancellationToken);
        }

        var value = await response.Content.ReadFromJsonAsync<RenameTagResult>(cancellationToken);

        return value is null
            ? StructureClientResult<RenameTagResult>.Failure(
                "client.empty-response",
                "The server accepted the change but returned nothing to show.")
            : StructureClientResult<RenameTagResult>.Success(value);
    }

    /// <inheritdoc />
    public async Task<StructureClientResult<int>> DeleteTagAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"{TagsBase}/{id}");
        var token = await TokenAsync(cancellationToken);

        message.Headers.Add(token.HeaderName, token.RequestToken);

        var response = await http.SendAsync(message, cancellationToken);

        if (!response.IsSuccessStatusCode) return await FailureAsync<int>(response, cancellationToken);

        return StructureClientResult<int>.Success(
            await response.Content.ReadFromJsonAsync<int>(cancellationToken));
    }

    /// <summary>Renders the filters an editor set, and nothing else.</summary>
    /// <remarks>
    /// Omitting the filters that are not set matters beyond tidiness: the URL is the shareable form
    /// of the search, and one carrying every default reads as a much more specific query than the
    /// editor actually ran.
    /// </remarks>
    private static string QueryString(SearchQuery query)
    {
        var parts = new List<string>(8);

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }

        Add("q", query.Text);
        Add("kind", query.Kind?.ToString());
        Add("templateId", query.TemplateId?.ToString(CultureInfo.InvariantCulture));
        Add("status", query.Status);
        Add("ownerUserId", query.OwnerUserId?.ToString(CultureInfo.InvariantCulture));
        Add("tag", query.Tag);
        Add("modifiedFrom", query.ModifiedFrom?.ToString("O", CultureInfo.InvariantCulture));
        Add("modifiedTo", query.ModifiedTo?.ToString("O", CultureInfo.InvariantCulture));
        Add("hasUnpublishedChanges", query.HasUnpublishedChanges?.ToString());
        if (query.PastReviewDate) parts.Add("pastReviewDate=true");
        if (query.Skip > 0) parts.Add($"skip={query.Skip}");
        Add("limit", query.Limit?.ToString(CultureInfo.InvariantCulture));

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
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
        _token ??= await http.GetFromJsonAsync<AntiforgeryTokenResponse>(
            "api/cms/v1/antiforgery-token",
            cancellationToken) ?? throw new InvalidOperationException(
                "The server did not issue an antiforgery token, so no tag change can be saved.");
}
