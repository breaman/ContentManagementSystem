using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Navigation;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="INavigationClient"/>, over the management API (task P8-16).
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// Every write carries an antiforgery token, fetched once per client instance and cached, for the
/// reason the structure client gives: the API is cookie-authenticated, so a write without one is
/// forgeable from any page a signed-in editor visits.
/// </remarks>
public sealed class HttpNavigationClient(HttpClient http) : INavigationClient
{
    private const string Base = "api/cms/v1/navigation/menus";

    private AntiforgeryTokenResponse? _token;

    /// <inheritdoc />
    public async Task<IReadOnlyList<NavigationMenuSummary>> GetMenusAsync(
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<NavigationMenuSummary>>(Base, cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<NavigationMenuDetail?> GetMenuAsync(int id, CancellationToken cancellationToken = default) =>
        GetAsync<NavigationMenuDetail>($"{Base}/{id}", cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<NavigationMenuDetail>> CreateMenuAsync(
        CreateNavigationMenuRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<CreateNavigationMenuRequest, NavigationMenuDetail>(
            HttpMethod.Post,
            Base,
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<NavigationMenuDetail>> UpdateMenuAsync(
        int id,
        UpdateNavigationMenuRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<UpdateNavigationMenuRequest, NavigationMenuDetail>(
            HttpMethod.Put,
            $"{Base}/{id}",
            request,
            cancellationToken);

    /// <inheritdoc />
    public async Task<StructureClientResult<int>> DeleteMenuAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        // The endpoint answers 204 with no body, so there is nothing to deserialize — and the count
        // it would have returned is of no use to a screen that is about to reload the list anyway.
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Base}/{id}");
        var token = await TokenAsync(cancellationToken);

        request.Headers.Add(token.HeaderName, token.RequestToken);

        var response = await http.SendAsync(request, cancellationToken);

        return response.IsSuccessStatusCode
            ? StructureClientResult<int>.Success(id)
            : await FailureAsync<int>(response, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StructureClientResult<NavigationMenuDetail>> AddItemAsync(
        int menuId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<SaveNavigationItemRequest, NavigationMenuDetail>(
            HttpMethod.Post,
            $"{Base}/{menuId}/items",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<NavigationMenuDetail>> UpdateItemAsync(
        int menuId,
        int itemId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<SaveNavigationItemRequest, NavigationMenuDetail>(
            HttpMethod.Put,
            $"{Base}/{menuId}/items/{itemId}",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<NavigationMenuDetail>> DeleteItemAsync(
        int menuId,
        int itemId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, NavigationMenuDetail>(
            HttpMethod.Delete,
            $"{Base}/{menuId}/items/{itemId}",
            body: null,
            cancellationToken);

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
        TBody? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null) request.Content = JsonContent.Create(body);

        var token = await TokenAsync(cancellationToken);

        request.Headers.Add(token.HeaderName, token.RequestToken);

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
        _token ??= await http.GetFromJsonAsync<AntiforgeryTokenResponse>(
            "api/cms/v1/antiforgery-token",
            cancellationToken) ?? throw new InvalidOperationException(
                "The server did not issue an antiforgery token, so no menu change can be saved.");
}
