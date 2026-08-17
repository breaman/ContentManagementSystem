using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// Arrange-phase helpers shared by the page API suites (tasks P2-16 to P2-19).
/// </summary>
/// <remarks>
/// Every fixture is built <em>through the API</em> rather than by inserting rows. That is slower and
/// it is the point: a test whose arrange step writes its own template and page proves nothing about
/// whether the endpoints an editor uses can produce the same state, and it is exactly the arrange
/// step that would keep passing after an endpoint broke.
/// </remarks>
public static class PageApiClient
{
    /// <summary>Route of the template collection.</summary>
    public const string Templates = $"{CmsApiEndpoints.BasePath}/templates";

    /// <summary>Route of the page collection.</summary>
    public const string Pages = $"{CmsApiEndpoints.BasePath}/pages";

    /// <summary>Creates a template carrying one plain-text zone, and returns it.</summary>
    /// <param name="client">A client holding <c>Structure.Edit</c>.</param>
    /// <param name="key">Key for the template, unique within the test class's database.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <param name="zoneKey">Key of the zone to add.</param>
    /// <param name="required">Whether the zone must be filled before the page can be published.</param>
    public static async Task<TemplateSummary> CreateTemplateAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken,
        string zoneKey = "body",
        bool required = false)
    {
        var created = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest(key, key),
            cancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var template = (await created.Content.ReadFromJsonAsync<TemplateDetail>(cancellationToken))!.Template;

        var zone = await client.PostAsJsonAsync(
            $"{Templates}/{template.Id}/zones",
            new CreateZoneRequest(zoneKey, zoneKey, FieldTypeKeys.PlainText, IsRequired: required),
            cancellationToken);

        zone.StatusCode.Should().Be(HttpStatusCode.Created);

        return template with { CurrentRevision = template.CurrentRevision + 1 };
    }

    /// <summary>Creates a page from a template, failing loudly if the API refused it.</summary>
    public static async Task<PageDetail> CreatePageAsync(
        HttpClient client,
        TemplateSummary template,
        string title,
        CancellationToken cancellationToken,
        int? parentId = null)
    {
        var response = await client.PostAsJsonAsync(
            Pages,
            new CreatePageRequest(template.Id, title, parentId),
            cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(cancellationToken));

        return (await response.Content.ReadFromJsonAsync<PageDetail>(cancellationToken))!;
    }

    /// <summary>
    /// Saves a draft with the payload's one zone filled in, using the page's current row version.
    /// </summary>
    /// <remarks>
    /// Re-reads the page first rather than trusting a token the caller is holding: several of these
    /// tests do something to a page between creating it and filling it in, and a stale precondition
    /// would fail the arrange step for a reason unrelated to what is under test.
    /// </remarks>
    public static async Task<HttpResponseMessage> FillZoneAsync(
        HttpClient client,
        int pageId,
        string zoneKey,
        string value,
        CancellationToken cancellationToken,
        string? templateKey = null,
        int? templateRevision = null)
    {
        var page = await client.GetFromJsonAsync<PageDetail>($"{Pages}/{pageId}", cancellationToken);

        var payload = Payload(
            templateKey ?? page!.Summary.TemplateKey,
            templateRevision ?? page!.TemplateRevision,
            zoneKey,
            value);

        return await SaveDraftAsync(client, pageId, payload, page!.RowVersion, cancellationToken);
    }

    /// <summary>Sends a draft save with an explicit <c>If-Match</c> precondition.</summary>
    public static async Task<HttpResponseMessage> SaveDraftAsync(
        HttpClient client,
        int pageId,
        string contentJson,
        string? rowVersion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Pages}/{pageId}/draft")
        {
            Content = JsonContent.Create(new SaveDraftRequest(contentJson, null)),
        };

        if (rowVersion is not null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>Builds a payload envelope with one plain-text zone filled in (spec section 6.2).</summary>
    public static string Payload(string templateKey, int templateRevision, string zoneKey, string value) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = ContentPayload.CurrentSchemaVersion,
            templateKey,
            templateRevision,
            zones = new Dictionary<string, object>
            {
                [zoneKey] = new { type = FieldTypeKeys.PlainText, value },
            },
        });

    /// <summary>Reads the stable codes out of a problem response, which is the part clients act on.</summary>
    public static async Task<IReadOnlyList<string>> CodesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);

        return problem!.Errors.Select(error => error.Code).ToList();
    }

    /// <summary>Reads a problem response whole, for the cases that assert on paths and warnings.</summary>
    public static async Task<ProblemBody> ProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        return (await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken))!;
    }

    /// <summary>An authenticated client with an antiforgery token already in hand.</summary>
    public static Task<HttpClient> ClientAsync(
        CmsApplicationFactory factory,
        CancellationToken cancellationToken,
        params string[] roles) =>
        CmsApplicationFactory.WithAntiforgeryTokenAsync(factory.CreateClientAs(roles), cancellationToken);

    /// <summary>A client holding every content permission Phase 2 checks.</summary>
    public static Task<HttpClient> AdministratorAsync(
        CmsApplicationFactory factory,
        CancellationToken cancellationToken) =>
        ClientAsync(factory, cancellationToken, CmsRoles.Administrator);

    /// <summary>The parts of an RFC 9457 body these suites assert on (spec section 22.2).</summary>
    /// <param name="Type">Problem type URI.</param>
    /// <param name="Title">Short, stable summary of the problem class.</param>
    /// <param name="Status">The HTTP status.</param>
    /// <param name="Detail">Human-readable explanation.</param>
    /// <param name="Errors">Everything that blocked the request.</param>
    /// <param name="Warnings">Anything non-blocking reported alongside.</param>
    /// <param name="Conflict">
    /// The state that beat the request, present only on a <c>409</c>. Left as a raw element because
    /// its shape depends on what was being written — a draft save gets the winning draft.
    /// </param>
    public sealed record ProblemBody(
        string? Type,
        string? Title,
        int Status,
        string? Detail,
        List<ApiDiagnostic> Errors,
        List<ApiDiagnostic> Warnings,
        JsonElement? Conflict = null);
}
