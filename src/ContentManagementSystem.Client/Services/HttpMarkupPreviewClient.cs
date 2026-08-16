using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="IMarkupPreviewClient"/>, over the management API (task P6-09).
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// The one write-shaped read in the backoffice, and it carries the antiforgery header for that
/// reason: it is a <c>POST</c> because the source is a zone's worth of prose that has no business in
/// a query string, and every <c>POST</c> in this API is guarded.
/// <para>
/// A failed request answers null rather than throwing. The caller is a preview pane, and the
/// worst thing it can do on a dropped connection is take the editor down around content nobody has
/// saved.
/// </para>
/// </remarks>
public sealed class HttpMarkupPreviewClient(HttpClient http) : IMarkupPreviewClient
{
    private const string Base = "api/cms/v1";

    private AntiforgeryTokenResponse? _token;

    /// <inheritdoc />
    public async Task<MarkupPreviewResult?> RenderAsync(
        MarkupPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{Base}/markup-preview")
            {
                Content = JsonContent.Create(request),
            };

            var token = await TokenAsync(cancellationToken);

            message.Headers.Add(token.HeaderName, token.RequestToken);

            using var response = await http.SendAsync(message, cancellationToken);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<MarkupPreviewResult>(cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or TaskCanceledException
                                              or InvalidOperationException
                                              or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain read, so no antiforgery token and no failure projection: an empty list means the
    /// banner says nothing, which is a banner missing rather than an editor broken.
    /// </remarks>
    public async Task<IReadOnlyList<SanitizationProfileDescriptor>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<SanitizationProfileDescriptor>>(
                $"{Base}/markup-preview/profiles",
                cancellationToken) ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or TaskCanceledException
                                              or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>Fetches the antiforgery pair once and reuses it for the client's lifetime.</summary>
    private async Task<AntiforgeryTokenResponse> TokenAsync(CancellationToken cancellationToken) =>
        _token ??= await http.GetFromJsonAsync<AntiforgeryTokenResponse>(
            $"{Base}/antiforgery-token",
            cancellationToken) ?? throw new InvalidOperationException(
                "The server did not issue an antiforgery token, so no preview can be rendered.");
}
