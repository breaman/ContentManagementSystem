using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.TestSupport;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// <c>/api/cms/v1/reusable</c> and <c>/references</c> over real HTTP (tasks P4-08 and P4-09).
/// </summary>
/// <remarks>
/// The service suite next door asserts what the operations do; this asserts the parts only the HTTP
/// surface has — the status codes, the permission on each route, the mandatory precondition on the
/// draft save, and that a refused publish comes back as <c>422</c> carrying the impact rather than as
/// a bare failure the client cannot build a dialog from.
/// <para>
/// Every fixture is built through the API rather than by inserting rows, for the reason
/// <see cref="PageApiClient"/> gives: an arrange step that writes its own rows keeps passing after
/// the endpoints an editor uses have broken.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class ReusableContentApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    /// <summary>Route of the reusable content collection.</summary>
    private const string Reusable = $"{CmsApiEndpoints.BasePath}/reusable";

    /// <summary>Route of the where-used endpoints.</summary>
    private const string References = $"{CmsApiEndpoints.BasePath}/references";

    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CreatingAnItemAnswers201WithItsLocationAndAnEmptyDraft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var blockType = await RawHtmlBlockTypeAsync(client, cancellationToken);

        var response = await client.PostAsJsonAsync(
            Reusable,
            new CreateReusableContentRequest(blockType.Id, "Site footer"),
            cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(cancellationToken));

        var created = (await response.Content.ReadFromJsonAsync<ReusableContentDetail>(cancellationToken))!;

        response.Headers.Location!.ToString().Should().EndWith($"{Reusable}/{created.Summary.Id}");

        // The key is generated from the name, and the first draft is schema-valid and empty — the
        // same two guarantees creating a page makes.
        created.Summary.Key.Should().Be("site-footer");
        created.Summary.DraftVersionNumber.Should().Be(1);
        created.Summary.PublishedVersionNumber.Should().BeNull("creating an item does not publish it");
        created.ContentJson.Should().Contain(blockType.Key);
    }

    [Fact]
    public async Task ADraftSaveWithNoPreconditionIsRefusedWith428()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await CreateItemAsync(client, "Unconditional", cancellationToken);

        var response = await client.PutAsJsonAsync(
            $"{Reusable}/{item.Summary.Id}/draft",
            new SaveDraftRequest(item.ContentJson, null),
            cancellationToken);

        // An unconditional save is a lost update waiting for two editors to open the same item — and
        // here the item is shared, so the two need not even have been looking at the same page.
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task TheDraftReadStampsTheTokenTheSaveHasToEchoBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await CreateItemAsync(client, "Round trip", cancellationToken);

        using var read = await client.GetAsync($"{Reusable}/{item.Summary.Id}", cancellationToken);

        read.Headers.ETag!.Tag.Trim('"').Should().Be(item.RowVersion);

        var saved = await client.PutAsJsonAsync(
            $"{Reusable}/{item.Summary.Id}/draft",
            new SaveDraftRequest(Html(item, "<p>Footer</p>"), item.RowVersion),
            cancellationToken);

        saved.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await saved.Content.ReadAsStringAsync(cancellationToken));

        // The response carries the next precondition, so a client saving twice in a row never has to
        // re-read to find out what token to send.
        saved.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishingPastAnUnacknowledgedBlastRadiusAnswers422CarryingTheCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await PublishedItemOnAPageAsync(client, "Banner", cancellationToken);

        await SaveDraftAsync(client, item, "<p>Second banner</p>", cancellationToken);

        var response = await client.PostAsJsonAsync(
            $"{Reusable}/{item.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response, cancellationToken);

        // The refusal is the dialog's content. A client that got only "422" would have nothing to put
        // in front of the person it is asking to confirm.
        //
        // It arrives in 'warnings' rather than 'errors', which is the whole shape of the mechanism:
        // nothing is wrong with the content, and the only thing standing between it and the public
        // site is that nobody has yet said they understand what publishing it does. Errors would
        // claim there is something to fix.
        problem.Errors.Should().BeEmpty("nothing about the content is invalid");
        problem.Warnings.Should().Contain(warning => warning.Code == ReusableCodes.BlastRadius);
    }

    [Fact]
    public async Task AnAcknowledgedPublishAnswersWithTheImpactItHad()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await PublishedItemOnAPageAsync(client, "Banner", cancellationToken);

        await SaveDraftAsync(client, item, "<p>Second banner</p>", cancellationToken);

        var response = await client.PostAsJsonAsync(
            $"{Reusable}/{item.Summary.Id}/publish",
            new PublishPageRequest(AcknowledgeWarnings: true),
            cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));

        var published = (await response.Content.ReadFromJsonAsync<ReusablePublishResult>(cancellationToken))!;

        // Three, not two: publishing snapshots the draft into a new row rather than promoting it, so
        // v1 is the draft that has never moved, v2 was the first publish, and this is v3.
        published.VersionNumber.Should().Be(3);
        published.Impact.AffectedPageCount.Should().Be(1);
    }

    [Fact]
    public async Task TheCheckReportsTheImpactWithoutPublishing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await PublishedItemOnAPageAsync(client, "Banner", cancellationToken);

        await SaveDraftAsync(client, item, "<p>Second banner</p>", cancellationToken);

        using var response = await client.PostAsync(
            $"{Reusable}/{item.Summary.Id}/validate",
            content: null,
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var validation = (await response.Content
            .ReadFromJsonAsync<ReusablePublishValidation>(cancellationToken))!;

        validation.CanPublish.Should().BeTrue();
        validation.Impact.AffectedPageCount.Should().Be(1);

        // A dry run, so the published pointer has not moved off the version the first publish made.
        var reread = await client.GetFromJsonAsync<ReusableContentDetail>(
            $"{Reusable}/{item.Summary.Id}",
            cancellationToken);

        reread!.Summary.PublishedVersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task DeletingAReferencedItemAnswers409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await PublishedItemOnAPageAsync(client, "Banner", cancellationToken);

        using var response = await client.DeleteAsync($"{Reusable}/{item.Summary.Id}", cancellationToken);

        // Conflict rather than 422: the request is well formed and the rule it breaks is about the
        // state of other rows, which is exactly what a conflict means here and on the page purge.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().Contain(error => error.Code == ReusableCodes.StillReferenced);
    }

    [Fact]
    public async Task TheWhereUsedEndpointAnswersForPagesAndItemsAlike()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await PublishedItemOnAPageAsync(client, "Banner", cancellationToken);

        var used = await client.GetFromJsonAsync<ReferenceImpact>(
            $"{References}/reusable/{item.Summary.Id}",
            cancellationToken);

        used!.AffectedPageCount.Should().Be(1);
        used.RequiresConfirmation.Should().BeTrue();

        // An entity nothing points at answers 200 with an empty impact rather than 404. "Nothing uses
        // this" is the answer the delete button needs, and distinguishing it from "no such entity"
        // would put an existence probe for every id behind a read permission that grants no such
        // thing.
        var unused = await client.GetFromJsonAsync<ReferenceImpact>(
            $"{References}/media/999999",
            cancellationToken);

        unused!.IsReferenced.Should().BeFalse();
    }

    [Fact]
    public async Task EveryWriteRouteRefusesACallerWithoutItsPermission()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var administrator = await AdministratorAsync(_factory, cancellationToken);
        var item = await CreateItemAsync(administrator, "Guarded", cancellationToken);

        // An Author edits drafts and neither publishes nor deletes, so the two routes that change
        // what visitors see are closed to them at the door rather than at the lock.
        using var author = await ClientAsync(_factory, cancellationToken, CmsRoles.Author);

        var published = await author.PostAsJsonAsync(
            $"{Reusable}/{item.Summary.Id}/publish",
            new PublishPageRequest(AcknowledgeWarnings: true),
            cancellationToken);

        published.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var deleted = await author.DeleteAsync($"{Reusable}/{item.Summary.Id}", cancellationToken);

        deleted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AWriteWithoutAnAntiforgeryTokenIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var administrator = await AdministratorAsync(_factory, cancellationToken);
        var item = await CreateItemAsync(administrator, "Unprotected", cancellationToken);

        // A client with an identity and no token, which is what a cross-site form post looks like.
        using var untokened = _factory.CreateClientAs(CmsRoles.Administrator);

        var response = await untokened.PostAsJsonAsync(
            $"{Reusable}/{item.Summary.Id}/publish",
            new PublishPageRequest(AcknowledgeWarnings: true),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The seeded built-in block type, read through the API a picker would use.</summary>
    private static async Task<BlockTypeSummary> RawHtmlBlockTypeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var blockTypes = await client.GetFromJsonAsync<List<BlockTypeSummary>>(
            $"{CmsApiEndpoints.BasePath}/block-types",
            cancellationToken);

        return blockTypes!.Single(candidate => candidate.Key == "rawHtml");
    }

    private static async Task<ReusableContentDetail> CreateItemAsync(
        HttpClient client,
        string name,
        CancellationToken cancellationToken)
    {
        var blockType = await RawHtmlBlockTypeAsync(client, cancellationToken);

        var response = await client.PostAsJsonAsync(
            Reusable,
            new CreateReusableContentRequest(blockType.Id, name),
            cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(cancellationToken));

        return (await response.Content.ReadFromJsonAsync<ReusableContentDetail>(cancellationToken))!;
    }

    /// <summary>Writes an HTML fragment into an item's draft and returns the item reloaded.</summary>
    private static async Task<ReusableContentDetail> SaveDraftAsync(
        HttpClient client,
        ReusableContentDetail item,
        string html,
        CancellationToken cancellationToken)
    {
        // Re-read first, because several of these tests publish between writes and a stale
        // precondition would fail the arrange step for a reason unrelated to what is under test.
        var current = (await client.GetFromJsonAsync<ReusableContentDetail>(
            $"{Reusable}/{item.Summary.Id}",
            cancellationToken))!;

        var response = await client.PutAsJsonAsync(
            $"{Reusable}/{current.Summary.Id}/draft",
            new SaveDraftRequest(Html(current, html), current.RowVersion),
            cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));

        return (await client.GetFromJsonAsync<ReusableContentDetail>(
            $"{Reusable}/{current.Summary.Id}",
            cancellationToken))!;
    }

    /// <summary>A published item placed on one published page — the fixture the impact tests need.</summary>
    private async Task<ReusableContentDetail> PublishedItemOnAPageAsync(
        HttpClient client,
        string name,
        CancellationToken cancellationToken)
    {
        var item = await CreateItemAsync(client, name, cancellationToken);

        item = await SaveDraftAsync(client, item, "<p>First banner</p>", cancellationToken);

        var published = await client.PostAsJsonAsync(
            $"{Reusable}/{item.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);

        // Nothing places it yet, so there is no blast radius to acknowledge — which is itself worth
        // asserting: the confirmation appears only when it means something.
        published.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await published.Content.ReadAsStringAsync(cancellationToken));

        var template = await PlacementTemplateAsync(client, $"places-{name.ToLowerInvariant()}", cancellationToken);

        var page = await CreatePageAsync(client, template, $"Page for {name}", cancellationToken);

        var payload = $$"""
            { "schemaVersion": 1, "templateKey": "{{template.Key}}",
              "templateRevision": {{page.TemplateRevision}},
              "zones": { "footer": { "type": "reusable", "reusableContentId": {{item.Summary.Id}},
                                     "pinnedVersionId": null } } }
            """;

        var saved = await client.PutAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/draft",
            new SaveDraftRequest(payload, page.RowVersion),
            cancellationToken);

        saved.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await saved.Content.ReadAsStringAsync(cancellationToken));

        var publishedPage = await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(AcknowledgeWarnings: true),
            cancellationToken);

        publishedPage.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await publishedPage.Content.ReadAsStringAsync(cancellationToken));

        return (await client.GetFromJsonAsync<ReusableContentDetail>(
            $"{Reusable}/{item.Summary.Id}",
            cancellationToken))!;
    }

    /// <summary>
    /// A template with one zone that holds a placement, created through the structure API.
    /// </summary>
    /// <remarks>
    /// <see cref="PageApiClient.CreateTemplateAsync"/> makes a plain-text zone, and a payload storing
    /// a <c>reusable</c> value in it is refused as a type mismatch — correctly, since a value has to
    /// be read by whatever wrote it and the schema says otherwise.
    /// </remarks>
    private static async Task<TemplateSummary> PlacementTemplateAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken)
    {
        var created = await client.PostAsJsonAsync(
            Templates,
            new CreateTemplateRequest(key, key),
            cancellationToken);

        created.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync(cancellationToken));

        var template = (await created.Content.ReadFromJsonAsync<TemplateDetail>(cancellationToken))!.Template;

        var zone = await client.PostAsJsonAsync(
            $"{Templates}/{template.Id}/zones",
            new CreateZoneRequest("footer", "Footer", FieldTypeKeys.Reusable),
            cancellationToken);

        zone.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await zone.Content.ReadAsStringAsync(cancellationToken));

        return template with { CurrentRevision = template.CurrentRevision + 1 };
    }

    /// <summary>A <c>rawHtml</c> item's payload with its one property filled in.</summary>
    private static string Html(ReusableContentDetail item, string html) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{item.Summary.BlockTypeKey}}",
          "templateRevision": {{item.BlockTypeRevision}},
          "zones": { "content": { "type": "html", "value": {{System.Text.Json.JsonSerializer.Serialize(html)}} } } }
        """;
}
