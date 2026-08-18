using System.Net;
using System.Net.Http.Json;
using System.Text;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Routing;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The redirect endpoints and the CSV pair a legacy migration runs through
/// (tasks P3-05 and P3-06, spec section 10.5).
/// </summary>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class RedirectApiTests(SqlServerFixture fixture)
{
    /// <summary>Route of the redirect collection.</summary>
    private const string Redirects = $"{CmsApiEndpoints.BasePath}/redirects";

    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Test]
    public async Task ARedirectIsCreatedListedAndDeleted()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var created = await client.PostAsJsonAsync(
            Redirects,
            new CreateRedirectRequest("/old-page", ToUrl: "/new-page", Notes: "site rebuild"),
            cancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var detail = (await created.Content.ReadFromJsonAsync<RedirectDetail>(cancellationToken))!;
        detail.FromUrl.Should().Be("/old-page");
        detail.ResolvedToUrl.Should().Be("/new-page");
        detail.IsAutomatic.Should().BeFalse("a person typed this one");

        var listed = await client.GetFromJsonAsync<PagedRedirects>(Redirects, cancellationToken);
        listed!.Items.Should().ContainSingle().Which.Id.Should().Be(detail.Id);

        var deleted = await client.DeleteAsync($"{Redirects}/{detail.Id}", cancellationToken);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<PagedRedirects>(Redirects, cancellationToken))!
            .Items.Should().BeEmpty();
    }

    [Test]
    public async Task ALoopIsRefusedWithTheProblemShapeTheClientSwitchesOn()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var refused = await client.PostAsJsonAsync(
            Redirects,
            new CreateRedirectRequest("/circle", ToUrl: "/circle"),
            cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await PageApiClient.ProblemAsync(refused, cancellationToken);
        problem.Errors.Should().ContainSingle().Which.Code.Should().Be(RoutingCodes.Loop);
    }

    [Test]
    public async Task AnAuthorMayReadRedirectsButNotChangeThem()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var author = await PageApiClient.ClientAsync(_factory, cancellationToken, CmsRoles.Author);

        // Reading is Content.Read; writing is Content.Publish, because a redirect reaches anonymous
        // visitors the instant it is saved with no draft or publish step in between.
        (await author.GetAsync(Redirects, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await author.PostAsJsonAsync(
            Redirects,
            new CreateRedirectRequest("/nope", ToUrl: "/somewhere"),
            cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnAnonymousCallerReachesNothing()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync(Redirects, cancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CsvGoesOutAndComesBackThroughTheEndpoints()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        const string csv = """
            from,to,status,notes
            /legacy/a,/a,301,
            /legacy/b,/b,302,moved temporarily
            /legacy/c,,301,
            """;

        var imported = await client.PostAsync(
            $"{Redirects}/import",
            new StringContent(csv, Encoding.UTF8, "text/csv"),
            cancellationToken);

        imported.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = (await imported.Content.ReadFromJsonAsync<ImportBody>(cancellationToken))!;
        summary.Created.Should().Be(2);
        summary.Skipped.Should().Be(1);

        // The warning naming the bad line survives the success. A count with no line numbers leaves
        // an operator searching a thousand-row spreadsheet by hand.
        summary.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(RoutingCodes.ImportRowInvalid);

        var exported = await client.GetAsync($"{Redirects}/export", cancellationToken);

        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        exported.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var body = await exported.Content.ReadAsStringAsync(cancellationToken);
        body.Should().StartWith("from,to,status,notes");
        body.Should().Contain("/legacy/a,/a,301");
        body.Should().Contain("/legacy/b,/b,302,moved temporarily");
    }

    /// <summary>The list response's shape, named so the deserializer has something to bind to.</summary>
    private sealed record PagedRedirects(List<RedirectDetail> Items, string? NextCursor);

    /// <summary>The import response's shape.</summary>
    private sealed record ImportBody(
        int Created,
        int Updated,
        int Skipped,
        List<Shared.Contracts.Api.ApiDiagnostic> Warnings);
}
