using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The preview-token endpoints (task P3-19, spec section 12.2).
/// </summary>
/// <remarks>
/// What is asserted here that no other suite can is the shape of the HTTP contract: which status a
/// refusal comes back as, and — the one that matters — that <em>nothing but the creating response
/// ever carries the secret</em>. A leak through the list endpoint would be invisible to the service
/// tests, because the service is not what serializes it.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PreviewTokenApiTests(SqlServerFixture fixture)
{
    /// <summary>Route of the preview-token collection.</summary>
    private const string Tokens = $"{CmsApiEndpoints.BasePath}/preview-tokens";

    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Test]
    public async Task ALinkIsIssuedListedAndRevoked()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var page = await PageAsync(client, "issued", cancellationToken);

        var created = await client.PostAsJsonAsync(
            Tokens,
            new CreatePreviewTokenRequest(page, Notes: "for the agency"),
            cancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var issued = (await created.Content.ReadFromJsonAsync<IssuedPreviewToken>(cancellationToken))!;

        issued.Token.Should().NotBeNullOrWhiteSpace();
        issued.Url.Should().Be($"/preview/s/{issued.Token}");
        issued.Summary.IsActive.Should().BeTrue();
        issued.Summary.Notes.Should().Be("for the agency");

        var listed = await client.GetFromJsonAsync<List<PreviewTokenSummary>>(
            $"{Tokens}?pageId={page}", cancellationToken);

        listed.Should().ContainSingle().Which.Id.Should().Be(issued.Summary.Id);

        var revoked = await client.DeleteAsync($"{Tokens}/{issued.Summary.Id}", cancellationToken);

        revoked.StatusCode.Should().Be(HttpStatusCode.OK);

        // Still listed, and now marked. The row is a record of who could once read an unpublished
        // page, which is exactly what revoking it makes worth keeping.
        var after = await client.GetFromJsonAsync<List<PreviewTokenSummary>>(
            $"{Tokens}?pageId={page}", cancellationToken);

        after.Should().ContainSingle().Which.Should().Match<PreviewTokenSummary>(
            token => token.RevokedOn != null && !token.IsActive);
    }

    [Test]
    public async Task NoResponseButTheCreatingOneEverCarriesTheSecret()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var page = await PageAsync(client, "secret", cancellationToken);

        var created = await client.PostAsJsonAsync(
            Tokens,
            new CreatePreviewTokenRequest(page),
            cancellationToken);

        var issued = (await created.Content.ReadFromJsonAsync<IssuedPreviewToken>(cancellationToken))!;

        // Read as raw text rather than through the typed shape: the assertion is about what went
        // over the wire, and deserializing into a record with no token member would hide a token
        // that was on it.
        var listed = await client.GetStringAsync($"{Tokens}?pageId={page}", cancellationToken);

        using var revoked = await client.DeleteAsync($"{Tokens}/{issued.Summary.Id}", cancellationToken);

        var revokedBody = await revoked.Content.ReadAsStringAsync(cancellationToken);

        // Half of acceptance criterion P3 #10, from the client's side. The other half — that the
        // database cannot produce it either — is asserted by the delivery suite.
        listed.Should().NotContain(issued.Token);
        revokedBody.Should().NotContain(issued.Token);
        listed.Should().NotContain("tokenHash").And.NotContain("TokenHash");
    }

    [Test]
    public async Task AnAuthorMayShareTheirOwnWorkAndAViewerMayNot()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var administrator = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var page = await PageAsync(administrator, "permissions", cancellationToken);

        using var author = await PageApiClient.ClientAsync(_factory, cancellationToken, CmsRoles.Author);
        using var viewer = await PageApiClient.ClientAsync(_factory, cancellationToken, CmsRoles.Viewer);

        var allowed = await author.PostAsJsonAsync(
            Tokens, new CreatePreviewTokenRequest(page), cancellationToken);

        var refused = await viewer.PostAsJsonAsync(
            Tokens, new CreatePreviewTokenRequest(page), cancellationToken);

        // Content.Edit, not Content.Publish. Sharing work for review is the ordinary act of whoever
        // is doing the work, and an author who could not get their own draft looked at would have no
        // use for the feature at all (spec section 21.1).
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // But a viewer may still see which links exist: reading is Content.Read.
        var listed = await viewer.GetAsync($"{Tokens}?pageId={page}", cancellationToken);

        listed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AnImpossibleExpiryIsRefusedWithTheProblemShapeTheClientSwitchesOn()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var page = await PageAsync(client, "expiry", cancellationToken);

        var refused = await client.PostAsJsonAsync(
            Tokens,
            new CreatePreviewTokenRequest(page, ExpiresInDays: 400),
            cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await PageApiClient.CodesAsync(refused, cancellationToken))
            .Should().Contain(PreviewCodes.ExpiryInvalid);
    }

    [Test]
    public async Task ALinkForAPageThatDoesNotExistIsNotFound()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var refused = await client.PostAsJsonAsync(
            Tokens,
            new CreatePreviewTokenRequest(987654),
            cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Creates a template and a page through the API, and returns the page's id.</summary>
    private static async Task<int> PageAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken)
    {
        var template = await PageApiClient.CreateTemplateAsync(client, key, cancellationToken);
        var page = await PageApiClient.CreatePageAsync(client, template, $"Page {key}", cancellationToken);

        return page.Summary.Id;
    }
}
