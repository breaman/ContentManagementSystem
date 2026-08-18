using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The template management API (task P1-21), driven end to end against a real database.
/// </summary>
/// <remarks>
/// One host and one database for the whole class: booting the server and migrating from empty costs
/// far more than the tests do, and they stay independent by giving every template its own key rather
/// than by starting from an empty table.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class TemplateApiTests(SqlServerFixture fixture)
{
    private const string Templates = $"{CmsApiEndpoints.BasePath}/templates";

    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Test]
    public async Task AnAnonymousCallerIsChallenged()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = _factory.CreateClientAs();

        var response = await client.GetAsync(Templates, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AViewerMayReadTemplatesButNotCreateOne()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await ClientAsync(cancellationToken, CmsRoles.Viewer);

        var list = await client.GetAsync(Templates, cancellationToken);
        var create = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest("viewer-attempt", "Viewer attempt"),
            cancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        // The content model is a developer's tool. A Viewer seeing it is fine; a Viewer changing it
        // would change what every editor is asked to fill in.
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CreatingATemplateReturnsItWithAnEmptyFirstRevision()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest("campaign-landing", "Marketing Landing Page", "Hero and body."),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TemplateDetail>(cancellationToken);

        created!.Template.Key.Should().Be("campaign-landing");
        created.Template.Name.Should().Be("Marketing Landing Page");
        created.Template.CurrentRevision.Should().Be(1);
        created.Template.ZoneCount.Should().Be(0);
        created.Template.IsEnabled.Should().BeTrue();
        // No deployed component declares this key, and the flag says so rather than waiting for the
        // startup reconciler to notice.
        created.Template.IsOrphaned.Should().BeTrue();
        created.Zones.Should().BeEmpty();

        response.Headers.Location!.ToString().Should().EndWith($"{Templates}/{created.Template.Id}");

        var reread = await client.GetFromJsonAsync<TemplateDetail>(
            $"{Templates}/{created.Template.Id}",
            cancellationToken);

        reread!.Template.Key.Should().Be("campaign-landing");
    }

    [Test]
    public async Task TheFirstRevisionExistsAndCapturesNoZones()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateAsync(client, "revision-baseline", cancellationToken);

        var revisions = await client.GetFromJsonAsync<List<TemplateRevisionSummary>>(
            $"{Templates}/{template.Id}/revisions",
            cancellationToken);

        revisions.Should().ContainSingle();
        revisions![0].RevisionNumber.Should().Be(1);
        revisions[0].IsCurrent.Should().BeTrue();
        revisions[0].ZoneCount.Should().Be(0);

        var revision = await client.GetFromJsonAsync<TemplateRevisionDetail>(
            $"{Templates}/{template.Id}/revisions/1",
            cancellationToken);

        revision!.TemplateKey.Should().Be("revision-baseline");
        // Content captures a revision number the moment it is created, so revision 1 has to be
        // there from the start even when there is nothing in it yet.
        revision.Zones.ValueKind.Should().Be(JsonValueKind.Array);
        revision.Zones.GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task ATakenKeyIsRefusedAsAConflict()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        await CreateAsync(client, "duplicate-key", cancellationToken);

        var response = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest("duplicate-key", "Something else"),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodesAsync(response, cancellationToken)).Should().Contain(StructureCodes.KeyDuplicate);
    }

    [Test]
    [Arguments("9lives")]
    [Arguments("has spaces")]
    [Arguments("trailing-")]
    [Arguments("double--hyphen")]
    public async Task AKeyOfTheWrongShapeIsRefused(string key)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest(key, "Badly keyed"),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(response, cancellationToken)).Should().Contain(StructureCodes.KeyFormat);
    }

    [Test]
    public async Task EveryBrokenRuleIsReportedAtOnce()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest("  ", "   "),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Returning one problem at a time turns filling in a form into a conversation.
        (await CodesAsync(response, cancellationToken))
            .Should().BeEquivalentTo([StructureCodes.KeyRequired, StructureCodes.NameRequired]);
    }

    [Test]
    public async Task RenamingTheDisplayNameSucceeds()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateAsync(client, "renameable", cancellationToken);

        var response = await client.PutAsJsonAsync(
            $"{Templates}/{template.Id}",
            new UpdateTemplateRequest(template.Key, "A better name", "Now with a description", IsEnabled: false),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<TemplateDetail>(cancellationToken);

        updated!.Template.Name.Should().Be("A better name");
        updated.Template.Description.Should().Be("Now with a description");
        updated.Template.IsEnabled.Should().BeFalse();
        updated.Template.Key.Should().Be("renameable");
        // Metadata is not structure: nothing about how stored content is read has changed, so no
        // revision is cut (spec section 8.5).
        updated.Template.CurrentRevision.Should().Be(1);
    }

    [Test]
    public async Task ChangingTheKeyIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateAsync(client, "immutable-key", cancellationToken);

        var response = await client.PutAsJsonAsync(
            $"{Templates}/{template.Id}",
            new UpdateTemplateRequest("immutable-key-renamed", template.Name),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(response, cancellationToken)).Should().Contain(StructureCodes.KeyImmutable);

        var reread = await client.GetFromJsonAsync<TemplateDetail>(
            $"{Templates}/{template.Id}",
            cancellationToken);

        reread!.Template.Key.Should().Be("immutable-key");
    }

    [Test]
    public async Task AWriteWithoutAnAntiforgeryTokenIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        // Deliberately not given a token: the management API is cookie-authenticated, so this is the
        // shape a cross-site request forgery arrives in.
        using var client = _factory.CreateClientAs(CmsRoles.Developer);

        var response = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest("forged", "Forged"),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodesAsync(response, cancellationToken)).Should().Contain("request.antiforgery");
    }

    [Test]
    public async Task AnUnknownTemplateOrRevisionIsNotFound()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateAsync(client, "known-template", cancellationToken);

        var unknownTemplate = await client.GetAsync($"{Templates}/987654", cancellationToken);
        var unknownRevision = await client.GetAsync(
            $"{Templates}/{template.Id}/revisions/99",
            cancellationToken);

        unknownTemplate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownRevision.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheListReportsWhatWasCreated()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        await CreateAsync(client, "listed-template", cancellationToken);

        var templates = await client.GetFromJsonAsync<List<TemplateSummary>>(Templates, cancellationToken);

        templates.Should().Contain(template => template.Key == "listed-template");
    }

    private async Task<HttpClient> DeveloperAsync(CancellationToken cancellationToken) =>
        await ClientAsync(cancellationToken, CmsRoles.Developer);

    private async Task<HttpClient> ClientAsync(CancellationToken cancellationToken, params string[] roles) =>
        await CmsApplicationFactory.WithAntiforgeryTokenAsync(_factory.CreateClientAs(roles), cancellationToken);

    private static async Task<TemplateSummary> CreateAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest(key, key),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<TemplateDetail>(cancellationToken))!.Template;
    }

    /// <summary>Reads the stable codes out of a problem response, which is the part clients act on.</summary>
    private static async Task<IReadOnlyList<string>> CodesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);

        return problem!.Errors.Select(error => error.Code).ToList();
    }

    /// <summary>The parts of an RFC 9457 body these tests assert on (spec section 22.2).</summary>
    private sealed record ProblemBody(
        string? Type,
        string? Title,
        int? Status,
        string? Detail,
        List<ApiDiagnostic> Errors,
        List<ApiDiagnostic> Warnings);
}
