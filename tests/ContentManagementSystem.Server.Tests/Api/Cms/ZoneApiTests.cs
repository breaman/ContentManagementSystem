using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The zone management API (task P1-22), driven end to end against a real database.
/// </summary>
/// <remarks>
/// The rules under test are the ones spec section 8.5 calls the contract that makes template
/// evolution safe, so they are asserted through the API rather than against the service: a rule that
/// holds in <c>ZoneService</c> and is bypassable by an endpoint is not enforced.
/// <para>
/// One host and one database for the class, as the template suite does, with every test giving its
/// template a distinct key rather than starting from an empty table.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class ZoneApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string Templates = $"{CmsApiEndpoints.BasePath}/templates";

    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task AddingAZoneStoresItAndCutsARevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-add", cancellationToken);

        var response = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest(
                "heroTitle",
                "Hero title",
                FieldTypeKeys.PlainText,
                Configuration("""{"maxLength":120}"""),
                Description: "Shown as the page heading.",
                IsRequired: true,
                Group: "Hero",
                SortOrder: 10),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var saved = await response.Content.ReadFromJsonAsync<ZoneSaveResult>(cancellationToken);

        saved!.Zone.Key.Should().Be("heroTitle");
        saved.Zone.Name.Should().Be("Hero title");
        saved.Zone.FieldTypeKey.Should().Be(FieldTypeKeys.PlainText);
        saved.Zone.IsRequired.Should().BeTrue();
        saved.Zone.Group.Should().Be("Hero");
        saved.Zone.SortOrder.Should().Be(10);
        saved.Zone.Configuration!.Value.GetProperty("maxLength").GetInt32().Should().Be(120);
        saved.Warnings.Should().BeEmpty();

        // Adding a zone changes how a payload is read, so it cuts a revision. Content authored
        // before it captured revision 1 and still validates against revision 1's snapshot.
        saved.CurrentRevision.Should().Be(2);

        response.Headers.Location!.ToString()
            .Should().EndWith($"{ZonesOf(template)}/{saved.Zone.Id}");
    }

    [Fact]
    public async Task ANewRevisionCapturesTheZoneAndTheOldOneDoesNot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-snapshot", cancellationToken);
        await AddZoneAsync(client, template, "body", FieldTypeKeys.RichText, cancellationToken);

        var before = await client.GetFromJsonAsync<TemplateRevisionDetail>(
            $"{Templates}/{template.Id}/revisions/1",
            cancellationToken);
        var after = await client.GetFromJsonAsync<TemplateRevisionDetail>(
            $"{Templates}/{template.Id}/revisions/2",
            cancellationToken);

        // The captured snapshot is what content is validated and rendered against, so this is the
        // assertion behind "a template change cannot retroactively alter what is live".
        before!.Zones.GetArrayLength().Should().Be(0);
        after!.Zones.GetArrayLength().Should().Be(1);
        after.Zones[0].GetProperty("key").GetString().Should().Be("body");
        after.Zones[0].GetProperty("fieldTypeKey").GetString().Should().Be(FieldTypeKeys.RichText);
    }

    [Fact]
    public async Task RenamingAZoneKeyIsRefusedAndRenamingItsLabelIsNot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-rename", cancellationToken);
        var zone = await AddZoneAsync(client, template, "body", FieldTypeKeys.PlainText, cancellationToken);

        var renameKey = await client.PutAsJsonAsync(
            $"{ZonesOf(template)}/{zone.Zone.Id}",
            new UpdateZoneRequest("bodyText", "Body", FieldTypeKeys.PlainText),
            cancellationToken);

        renameKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(renameKey, cancellationToken)).Should().Contain(StructureCodes.KeyImmutable);

        var renameLabel = await client.PutAsJsonAsync(
            $"{ZonesOf(template)}/{zone.Zone.Id}",
            new UpdateZoneRequest("body", "Body copy", FieldTypeKeys.PlainText, Description: "Main text."),
            cancellationToken);

        renameLabel.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await renameLabel.Content.ReadFromJsonAsync<ZoneSaveResult>(cancellationToken);

        updated!.Zone.Key.Should().Be("body");
        updated.Zone.Name.Should().Be("Body copy");
        updated.Zone.Description.Should().Be("Main text.");
        // A label is not structure. Cutting a revision for it would bury the changes revisions
        // exist to record (spec section 8.5).
        updated.CurrentRevision.Should().Be(2);
    }

    [Fact]
    public async Task ChangingWhatAZoneRequiresCutsARevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-structural", cancellationToken);
        var zone = await AddZoneAsync(client, template, "body", FieldTypeKeys.PlainText, cancellationToken);

        var response = await client.PutAsJsonAsync(
            $"{ZonesOf(template)}/{zone.Zone.Id}",
            new UpdateZoneRequest(
                "body",
                "Body",
                FieldTypeKeys.PlainText,
                Configuration("""{"maxLength":50}"""),
                IsRequired: true),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<ZoneSaveResult>(cancellationToken);

        updated!.Zone.IsRequired.Should().BeTrue();
        updated.Zone.Configuration!.Value.GetProperty("maxLength").GetInt32().Should().Be(50);
        // Both changes alter how a stored value is judged, so content keeps validating against the
        // revision it captured until it is republished.
        updated.CurrentRevision.Should().Be(3);
    }

    [Fact]
    public async Task ChangingAZonesFieldTypeIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-retype", cancellationToken);
        var zone = await AddZoneAsync(client, template, "body", FieldTypeKeys.PlainText, cancellationToken);

        var response = await client.PutAsJsonAsync(
            $"{ZonesOf(template)}/{zone.Zone.Id}",
            new UpdateZoneRequest("body", "Body", FieldTypeKeys.Number),
            cancellationToken);

        // Spec section 8.5 makes this a content migration with an explicit converter choice. There
        // is nothing to convert yet and no job to run it, so it is refused rather than half-done.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(response, cancellationToken)).Should().Contain(StructureCodes.FieldTypeImmutable);

        var reread = await client.GetFromJsonAsync<ZoneDefinition>(
            $"{ZonesOf(template)}/{zone.Zone.Id}",
            cancellationToken);

        reread!.FieldTypeKey.Should().Be(FieldTypeKeys.PlainText);
    }

    [Fact]
    public async Task RemovingAZoneCutsARevisionAndReportsTheKeyItRetains()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-remove", cancellationToken);
        var zone = await AddZoneAsync(client, template, "obsolete", FieldTypeKeys.PlainText, cancellationToken);

        var response = await client.DeleteAsync($"{ZonesOf(template)}/{zone.Zone.Id}", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var removed = await response.Content.ReadFromJsonAsync<ZoneRemovalResult>(cancellationToken);

        removed!.Key.Should().Be("obsolete");
        removed.CurrentRevision.Should().Be(3);

        var remaining = await client.GetFromJsonAsync<List<ZoneDefinition>>(ZonesOf(template), cancellationToken);

        remaining.Should().BeEmpty();

        // The definition goes; the earlier revision that captured it stays, which is what lets a
        // page authored against it still be read (spec section 8.5).
        var captured = await client.GetFromJsonAsync<TemplateRevisionDetail>(
            $"{Templates}/{template.Id}/revisions/2",
            cancellationToken);

        captured!.Zones.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ReusingAZoneKeyWithinATemplateIsRefusedButNotAcrossTemplates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var first = await CreateTemplateAsync(client, "zone-key-first", cancellationToken);
        var second = await CreateTemplateAsync(client, "zone-key-second", cancellationToken);
        await AddZoneAsync(client, first, "body", FieldTypeKeys.PlainText, cancellationToken);

        var duplicate = await client.PostAsJsonAsync(
            ZonesOf(first),
            new CreateZoneRequest("body", "Body again", FieldTypeKeys.RichText),
            cancellationToken);

        var elsewhere = await client.PostAsJsonAsync(
            ZonesOf(second),
            new CreateZoneRequest("body", "Body", FieldTypeKeys.RichText),
            cancellationToken);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodesAsync(duplicate, cancellationToken)).Should().Contain(StructureCodes.KeyDuplicate);
        // A zone key addresses a value inside one template's payload. Two templates may each have a
        // "body" zone, of different types.
        elsewhere.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AConfigurationTheFieldTypeCannotHonourIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-config", cancellationToken);

        var unknownSetting = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("a", "A", FieldTypeKeys.PlainText, Configuration("""{"maxlength":10}""")),
            cancellationToken);

        var badPattern = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("b", "B", FieldTypeKeys.PlainText, Configuration("""{"pattern":"[unclosed"}""")),
            cancellationToken);

        var inverted = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest(
                "c",
                "C",
                FieldTypeKeys.PlainText,
                Configuration("""{"minLength":10,"maxLength":2}""")),
            cancellationToken);

        // Configuration is closed: a mistyped setting is a save error, not a line that persists and
        // silently does nothing (P1-12).
        unknownSetting.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(unknownSetting, cancellationToken))
            .Should().Contain(FieldConfigurationCodes.UnknownSetting);

        badPattern.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(badPattern, cancellationToken)).Should().Contain(FieldConfigurationCodes.SettingFormat);

        inverted.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(inverted, cancellationToken)).Should().Contain(FieldConfigurationCodes.RangeInverted);

        var zones = await client.GetFromJsonAsync<List<ZoneDefinition>>(ZonesOf(template), cancellationToken);

        zones.Should().BeEmpty();
    }

    [Fact]
    public async Task ASettingWhosePhaseHasNotShippedIsStoredAndWarnedAbout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-early-config", cancellationToken);

        var response = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest(
                "photo",
                "Photo",
                FieldTypeKeys.Media,
                Configuration("""{"minWidth":800}""")),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var saved = await response.Content.ReadFromJsonAsync<ZoneSaveResult>(cancellationToken);

        // Accepted and stored, because refusing it makes a developer build half a content model and
        // come back. Reported, because a setting that quietly does nothing is worse than one that
        // says when it will start (P1-12).
        saved!.Zone.Configuration!.Value.GetProperty("minWidth").GetInt32().Should().Be(800);
        saved.Warnings.Should().ContainSingle();
        saved.Warnings[0].Code.Should().Be(FieldConfigurationCodes.NotEnforced);
        saved.Warnings[0].Property.Should().Be("minWidth");
    }

    [Fact]
    public async Task AZoneBoundToAnUnregisteredFieldTypeIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-unknown-type", cancellationToken);

        var unknown = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("body", "Body", "carouselOfDoom"),
            cancellationToken);

        var missing = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("body", "Body", "  "),
            cancellationToken);

        // Unlike delivery, which has to be forgiving about a payload it already stored, a zone save
        // is the moment before anything is stored.
        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(unknown, cancellationToken)).Should().Contain(FieldConfigurationCodes.UnknownFieldType);

        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(missing, cancellationToken)).Should().Contain(StructureCodes.FieldTypeRequired);
    }

    [Fact]
    public async Task EveryBrokenRuleIsReportedAtOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-many-problems", cancellationToken);

        var response = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("9lives", "   ", FieldTypeKeys.PlainText, Configuration("""{"nope":1}""")),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(response, cancellationToken)).Should().BeEquivalentTo([
            StructureCodes.KeyFormat,
            StructureCodes.NameRequired,
            FieldConfigurationCodes.UnknownSetting,
        ]);
    }

    [Fact]
    public async Task AnEmptyConfigurationIsStoredAsNone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-empty-config", cancellationToken);

        var response = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("body", "Body", FieldTypeKeys.PlainText, Configuration("{}")),
            cancellationToken);

        var saved = await response.Content.ReadFromJsonAsync<ZoneSaveResult>(cancellationToken);

        // An absent configuration and an empty one mean the same thing, and collapsing them keeps
        // "{}" out of every revision snapshot where it would read as a change.
        saved!.Zone.Configuration.Should().BeNull();

        var revision = await client.GetFromJsonAsync<TemplateRevisionDetail>(
            $"{Templates}/{template.Id}/revisions/2",
            cancellationToken);

        revision!.Zones[0].TryGetProperty("configuration", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ZonesAreListedInEditorOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(client, "zone-order", cancellationToken);

        await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("footer", "Footer", FieldTypeKeys.RichText, SortOrder: 30),
            cancellationToken);
        await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("hero", "Hero", FieldTypeKeys.PlainText, SortOrder: 10),
            cancellationToken);

        var zones = await client.GetFromJsonAsync<List<ZoneDefinition>>(ZonesOf(template), cancellationToken);

        zones!.Select(zone => zone.Key).Should().Equal("hero", "footer");
    }

    [Fact]
    public async Task AViewerMayReadZonesButNotChangeThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(developer, "zone-viewer", cancellationToken);
        var zone = await AddZoneAsync(developer, template, "body", FieldTypeKeys.PlainText, cancellationToken);

        using var viewer = await ClientAsync(cancellationToken, CmsRoles.Viewer);

        var list = await viewer.GetAsync(ZonesOf(template), cancellationToken);
        var create = await viewer.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("sneaky", "Sneaky", FieldTypeKeys.PlainText),
            cancellationToken);
        var delete = await viewer.DeleteAsync($"{ZonesOf(template)}/{zone.Zone.Id}", cancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AWriteWithoutAnAntiforgeryTokenIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await DeveloperAsync(cancellationToken);
        var template = await CreateTemplateAsync(developer, "zone-forged", cancellationToken);

        // Deliberately not given a token: the management API is cookie-authenticated, so this is the
        // shape a cross-site request forgery arrives in.
        using var forger = _factory.CreateClientAs(CmsRoles.Developer);

        var response = await forger.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest("forged", "Forged", FieldTypeKeys.PlainText),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodesAsync(response, cancellationToken)).Should().Contain("request.antiforgery");
    }

    [Fact]
    public async Task AZoneOfAnotherTemplateIsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var owner = await CreateTemplateAsync(client, "zone-owner", cancellationToken);
        var other = await CreateTemplateAsync(client, "zone-other", cancellationToken);
        var zone = await AddZoneAsync(client, owner, "body", FieldTypeKeys.PlainText, cancellationToken);

        var wrongTemplate = await client.GetAsync($"{ZonesOf(other)}/{zone.Zone.Id}", cancellationToken);
        var unknownTemplate = await client.GetAsync($"{Templates}/987654/zones", cancellationToken);
        var unknownZone = await client.GetAsync($"{ZonesOf(owner)}/987654", cancellationToken);

        // The pair is the address. Answering anything but "not found" for a zone of another template
        // would confirm the existence of a row the caller did not ask about.
        wrongTemplate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownTemplate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownZone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static string ZonesOf(TemplateSummary template) => $"{Templates}/{template.Id}/zones";

    /// <summary>Parses configuration the way a client sends it — as an object, not a string.</summary>
    private static JsonElement Configuration(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<HttpClient> DeveloperAsync(CancellationToken cancellationToken) =>
        await ClientAsync(cancellationToken, CmsRoles.Developer);

    private async Task<HttpClient> ClientAsync(CancellationToken cancellationToken, params string[] roles) =>
        await CmsApplicationFactory.WithAntiforgeryTokenAsync(_factory.CreateClientAs(roles), cancellationToken);

    private static async Task<TemplateSummary> CreateTemplateAsync(
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

    private static async Task<ZoneSaveResult> AddZoneAsync(
        HttpClient client,
        TemplateSummary template,
        string key,
        string fieldTypeKey,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            ZonesOf(template),
            new CreateZoneRequest(key, key, fieldTypeKey),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<ZoneSaveResult>(cancellationToken))!;
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
    private sealed record ProblemBody(List<ApiDiagnostic> Errors, List<ApiDiagnostic> Warnings);
}
