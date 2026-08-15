using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.TestSupport;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// Deleting a template, and the guard that usually stops it (task P1-32, spec section 8.5).
/// </summary>
/// <remarks>
/// The last of Phase 1's template evolution rules, and the one that could not be written until
/// <c>Page</c> existed in <c>P2-01</c> — which is why the verb itself was withheld rather than
/// shipped with a guard it could not yet enforce.
/// <para>
/// Every fixture is built through the API, as the other page suites are. It matters more here than
/// usual: what is under test is whether the endpoints an editor and a developer actually use can
/// reach a state where a template is deletable, and an arrange step that inserted its own rows would
/// answer a different question.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class TemplateDeleteApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task ATemplateNoPageUsesIsDeletedWithItsZonesAndRevisions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "delete-unused", cancellationToken);

        var deleted = await client.DeleteAsync($"{Templates}/{template.Id}", cancellationToken);

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync($"{Templates}/{template.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The zones and the captured revisions go with it. They are Restrict-ed foreign keys, so a
        // delete that forgot them would fail at the database rather than leave them behind — but a
        // template row gone while its revisions remain is unreachable history either way.
        (await client.GetAsync($"{Templates}/{template.Id}/revisions", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingATemplateAPageStillUsesIsRefusedAndNamesThePage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "delete-in-use", cancellationToken);

        await CreatePageAsync(client, template, "Quarterly Report", cancellationToken);

        var refused = await client.DeleteAsync($"{Templates}/{template.Id}", cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await ProblemAsync(refused, cancellationToken);

        problem.Errors.Should().ContainSingle().Which.Code.Should().Be(StructureCodes.InUse);

        // Named, not merely counted. "1 page uses this template" leaves the developer to go and
        // find it, and the whole point of refusing here is that the remedy is actionable.
        problem.Errors[0].Message.Should().Contain("Quarterly Report");

        // And the template is still there afterwards, which is the part a status code alone does
        // not say: a refusal that had already removed the zones would be worse than a delete.
        (await client.GetAsync($"{Templates}/{template.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task APageInTheRecycleBinStillBlocksTheDeleteUntilTheBinIsEmptied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "delete-recycled", cancellationToken);
        var page = await CreatePageAsync(client, template, "Retired Notice", cancellationToken);

        (await client.DeleteAsync($"{Pages}/{page.Summary.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await client.DeleteAsync($"{Templates}/{template.Id}", cancellationToken);

        // Spec section 8.5 words the rule as non-deleted pages. A recycled page keeps its
        // TemplateId and can be restored, so the narrow reading would turn a restore into a page
        // with no schema — and the foreign key would refuse the delete anyway, handing the caller a
        // database error in place of an answer.
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await ProblemAsync(refused, cancellationToken);

        problem.Errors[0].Code.Should().Be(StructureCodes.InUse);
        problem.Errors[0].Message.Should().Contain("recycle bin", "the remedy is what the caller needs");

        // Purging it is that remedy, and it works.
        (await client.DeleteAsync($"{Pages}/{page.Summary.Id}/permanent", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.DeleteAsync($"{Templates}/{template.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AnEditorWhoMayNotChangeTheContentModelMayNotDeleteATemplate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(developer, "delete-forbidden", cancellationToken);

        using var editor = await ClientAsync(_factory, cancellationToken, CmsRoles.Editor);

        // Deleting a template is a structural change, not a content one, and Structure.Edit is what
        // gates it — an Editor may publish and empty the recycle bin and still not do this.
        (await editor.DeleteAsync($"{Templates}/{template.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await developer.GetAsync($"{Templates}/{template.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeletingATemplateThatIsNotThereIsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        (await client.DeleteAsync($"{Templates}/999999", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ADeleteWithNoAntiforgeryTokenIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "delete-antiforgery", cancellationToken);

        // The API is cookie-authenticated, so a write with no token is forgeable from any page a
        // signed-in developer visits — and this write destroys a content model.
        using var untokened = _factory.CreateClientAs(CmsRoles.Administrator);

        (await untokened.DeleteAsync($"{Templates}/{template.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.GetAsync($"{Templates}/{template.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
