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
/// The block type, property, and composition management API (tasks P1-23 and P1-24).
/// </summary>
/// <remarks>
/// The rules worth the cost of a real database are the ones that span tables: that a composition
/// flattens into a block type's captured snapshot, that editing a shared group recuts every block
/// type composing it, and that a key can only be defined once across both. None of those can be
/// asserted against a single service in isolation.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class BlockTypeApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string BlockTypes = $"{CmsApiEndpoints.BasePath}/block-types";
    private const string Compositions = $"{CmsApiEndpoints.BasePath}/compositions";
    private const string FieldTypes = $"{CmsApiEndpoints.BasePath}/field-types";

    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CreatingABlockTypeReturnsItWithAnEmptyFirstRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);

        var response = await client.PostAsJsonAsync(
            BlockTypes,
            new CreateBlockTypeRequest("promo-banner", "Hero banner", "A full-width headline.", "image", "{headline}"),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<BlockTypeDetail>(cancellationToken);

        created!.BlockType.Key.Should().Be("promo-banner");
        created.BlockType.CurrentRevision.Should().Be(1);
        created.BlockType.IconKey.Should().Be("image");
        created.BlockType.SummaryTemplate.Should().Be("{headline}");
        // No deployed component claims this key yet, and a block type created in the backoffice says
        // so rather than waiting for the reconciler to notice.
        created.BlockType.IsOrphaned.Should().BeTrue();
        created.BlockType.IsBuiltIn.Should().BeFalse();
        created.Properties.Should().BeEmpty();
        created.EffectiveProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task AddingAPropertyCutsARevisionThatCapturesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "quote", cancellationToken);

        var saved = await AddPropertyAsync(client, blockType, "attribution", FieldTypeKeys.PlainText, cancellationToken);

        saved.Property.Key.Should().Be("attribution");
        saved.Property.CompositionKey.Should().BeNull();
        saved.CurrentRevision.Should().Be(2);

        var revision = await client.GetFromJsonAsync<BlockTypeRevisionDetail>(
            $"{BlockTypes}/{blockType.Id}/revisions/2",
            cancellationToken);

        revision!.Properties.GetArrayLength().Should().Be(1);
        revision.Properties[0].GetProperty("key").GetString().Should().Be("attribution");
        revision.Revision.PropertyCount.Should().Be(1);
    }

    [Fact]
    public async Task APropertyKeyAndFieldTypeAreBothImmutable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "immutable-props", cancellationToken);
        var property = await AddPropertyAsync(client, blockType, "body", FieldTypeKeys.PlainText, cancellationToken);

        var rekey = await client.PutAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/properties/{property.Property.Id}",
            new UpdatePropertyRequest("bodyText", "Body", FieldTypeKeys.PlainText),
            cancellationToken);

        var retype = await client.PutAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/properties/{property.Property.Id}",
            new UpdatePropertyRequest("body", "Body", FieldTypeKeys.Number),
            cancellationToken);

        rekey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(rekey, cancellationToken)).Should().Contain(StructureCodes.KeyImmutable);

        // The same rule zones get, for the same reason: a block property and a zone are one thing at
        // validation time.
        retype.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(retype, cancellationToken)).Should().Contain(StructureCodes.FieldTypeImmutable);
    }

    [Fact]
    public async Task RemovingAPropertyCutsARevisionAndKeepsTheOldOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "shrinking", cancellationToken);
        var property = await AddPropertyAsync(client, blockType, "obsolete", FieldTypeKeys.PlainText, cancellationToken);

        var response = await client.DeleteAsync(
            $"{BlockTypes}/{blockType.Id}/properties/{property.Property.Id}",
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var removed = await response.Content.ReadFromJsonAsync<PropertyRemovalResult>(cancellationToken);

        removed!.Key.Should().Be("obsolete");
        removed.CurrentRevision.Should().Be(3);

        var captured = await client.GetFromJsonAsync<BlockTypeRevisionDetail>(
            $"{BlockTypes}/{blockType.Id}/revisions/2",
            cancellationToken);

        // Blocks authored against revision 2 still read their value; the definition going away is
        // not the value going away (spec section 8.5).
        captured!.Properties.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task TheBuiltInBlockTypeRefusesStructuralChangesButAcceptsARename()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);

        var all = await client.GetFromJsonAsync<List<BlockTypeSummary>>(BlockTypes, cancellationToken);
        var builtIn = all!.Single(blockType => blockType.IsBuiltIn);

        var addProperty = await client.PostAsJsonAsync(
            $"{BlockTypes}/{builtIn.Id}/properties",
            new CreatePropertyRequest("extra", "Extra", FieldTypeKeys.PlainText),
            cancellationToken);

        var rename = await client.PutAsJsonAsync(
            $"{BlockTypes}/{builtIn.Id}",
            new UpdateBlockTypeRequest(builtIn.Key, "Free-form HTML"),
            cancellationToken);

        // Its renderer expects exactly the property set it ships with, so structure is fixed…
        addProperty.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(addProperty, cancellationToken)).Should().Contain(StructureCodes.BuiltInImmutable);

        // …while what an editor sees it called is nobody's dependency.
        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rename.Content.ReadFromJsonAsync<BlockTypeDetail>(cancellationToken))!
            .BlockType.Name.Should().Be("Free-form HTML");
    }

    [Fact]
    public async Task AComposedGroupFlattensIntoTheBlockTypeAfterItsOwnProperties()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "composed-host", cancellationToken);
        await AddPropertyAsync(client, blockType, "headline", FieldTypeKeys.PlainText, cancellationToken);

        var composition = await CreateCompositionAsync(client, "spacing-options", cancellationToken);
        await AddCompositionPropertyAsync(client, composition, "marginTop", cancellationToken);

        var attached = await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/compositions",
            new AttachCompositionRequest(composition.Id),
            cancellationToken);

        attached.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await attached.Content.ReadFromJsonAsync<BlockTypeDetail>(cancellationToken);

        detail!.Properties.Should().ContainSingle();
        detail.Compositions.Should().ContainSingle();
        detail.EffectiveProperties.Select(property => property.Key).Should().Equal("headline", "marginTop");
        // A composed property is not editable on the host: editing it there would fork one
        // definition into many, so the client is told which is which.
        detail.EffectiveProperties[1].CompositionKey.Should().Be("spacing-options");

        var revision = await client.GetFromJsonAsync<BlockTypeRevisionDetail>(
            $"{BlockTypes}/{blockType.Id}/revisions/{detail.BlockType.CurrentRevision}",
            cancellationToken);

        // The snapshot has the composition flattened away, which is what makes a published block
        // immune to a later edit of the group.
        revision!.Properties.GetArrayLength().Should().Be(2);
        revision.Properties[1].GetProperty("key").GetString().Should().Be("marginTop");
    }

    [Fact]
    public async Task EditingASharedGroupRecutsEveryBlockTypeComposingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var composition = await CreateCompositionAsync(client, "analytics-attrs", cancellationToken);

        var first = await CreateBlockTypeAsync(client, "recut-first", cancellationToken);
        var second = await CreateBlockTypeAsync(client, "recut-second", cancellationToken);

        foreach (var blockType in new[] { first, second })
        {
            (await client.PostAsJsonAsync(
                $"{BlockTypes}/{blockType.Id}/compositions",
                new AttachCompositionRequest(composition.Id),
                cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var response = await client.PostAsJsonAsync(
            $"{Compositions}/{composition.Id}/properties",
            new CreatePropertyRequest("campaign", "Campaign", FieldTypeKeys.PlainText),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var saved = await response.Content.ReadFromJsonAsync<CompositionPropertySaveResult>(cancellationToken);

        // A composition is not itself revisioned — nothing addresses it from a payload — so what the
        // caller is told is where the edit landed.
        saved!.AffectedBlockTypeKeys.Should().Equal("recut-first", "recut-second");

        foreach (var blockType in new[] { first, second })
        {
            var reread = await client.GetFromJsonAsync<BlockTypeDetail>(
                $"{BlockTypes}/{blockType.Id}",
                cancellationToken);

            reread!.BlockType.CurrentRevision.Should().Be(3);
            reread.EffectiveProperties.Select(property => property.Key).Should().Equal("campaign");
        }
    }

    [Fact]
    public async Task AKeyCannotBeDefinedTwiceInOneBlockInstance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "collision-host", cancellationToken);
        await AddPropertyAsync(client, blockType, "spacing", FieldTypeKeys.PlainText, cancellationToken);

        var composition = await CreateCompositionAsync(client, "collision-group", cancellationToken);
        await AddCompositionPropertyAsync(client, composition, "spacing", cancellationToken);

        var attach = await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/compositions",
            new AttachCompositionRequest(composition.Id),
            cancellationToken);

        attach.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(attach, cancellationToken)).Should().Contain(StructureCodes.CompositionCollision);
    }

    [Fact]
    public async Task AGroupPropertyThatWouldCollideOnAHostIsRefusedAtTheGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "late-collision-host", cancellationToken);
        await AddPropertyAsync(client, blockType, "caption", FieldTypeKeys.PlainText, cancellationToken);

        var composition = await CreateCompositionAsync(client, "late-collision-group", cancellationToken);

        (await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/compositions",
            new AttachCompositionRequest(composition.Id),
            cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsJsonAsync(
            $"{Compositions}/{composition.Id}/properties",
            new CreatePropertyRequest("caption", "Caption", FieldTypeKeys.PlainText),
            cancellationToken);

        // The collision is not inside the group, it is where the group lands — and refusing it here
        // is the difference between one clear error and a block type whose editor silently breaks.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(response, cancellationToken)).Should().Contain(StructureCodes.CompositionCollision);
    }

    [Fact]
    public async Task ComposingTheSameGroupTwiceIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "double-compose", cancellationToken);
        var composition = await CreateCompositionAsync(client, "double-compose-group", cancellationToken);

        await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/compositions",
            new AttachCompositionRequest(composition.Id),
            cancellationToken);

        var again = await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/compositions",
            new AttachCompositionRequest(composition.Id),
            cancellationToken);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodesAsync(again, cancellationToken)).Should().Contain(StructureCodes.CompositionDuplicate);
    }

    [Fact]
    public async Task DetachingAGroupRemovesItsPropertiesFromTheNextRevisionOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "detach-host", cancellationToken);
        var composition = await CreateCompositionAsync(client, "detach-group", cancellationToken);
        await AddCompositionPropertyAsync(client, composition, "theme", cancellationToken);

        var attached = await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/compositions",
            new AttachCompositionRequest(composition.Id),
            cancellationToken);

        var withGroup = (await attached.Content.ReadFromJsonAsync<BlockTypeDetail>(cancellationToken))!
            .BlockType.CurrentRevision;

        var detached = await client.DeleteAsync(
            $"{BlockTypes}/{blockType.Id}/compositions/{composition.Id}",
            cancellationToken);

        detached.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await detached.Content.ReadFromJsonAsync<BlockTypeDetail>(cancellationToken);

        after!.EffectiveProperties.Should().BeEmpty();
        after.BlockType.CurrentRevision.Should().Be(withGroup + 1);

        var before = await client.GetFromJsonAsync<BlockTypeRevisionDetail>(
            $"{BlockTypes}/{blockType.Id}/revisions/{withGroup}",
            cancellationToken);

        before!.Properties.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task DeletingAComposedGroupIsRefusedAndNamesWhatIsInTheWay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "guard-host", cancellationToken);
        var composition = await CreateCompositionAsync(client, "guard-group", cancellationToken);

        await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/compositions",
            new AttachCompositionRequest(composition.Id),
            cancellationToken);

        var blocked = await client.DeleteAsync($"{Compositions}/{composition.Id}", cancellationToken);

        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodesAsync(blocked, cancellationToken)).Should().Contain(StructureCodes.InUse);

        await client.DeleteAsync($"{BlockTypes}/{blockType.Id}/compositions/{composition.Id}", cancellationToken);

        var allowed = await client.DeleteAsync($"{Compositions}/{composition.Id}", cancellationToken);

        // The one delete this phase can honestly ship: its guard is a join table that exists, unlike
        // the page table a template delete would have to ask.
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task TheFieldTypeRegistryIsServedWithEachConfigurationSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);

        var all = await client.GetFromJsonAsync<List<FieldTypeDescriptor>>(FieldTypes, cancellationToken);

        all.Should().NotBeEmpty();
        all!.Select(descriptor => descriptor.Key).Should().Contain(
            [FieldTypeKeys.PlainText, FieldTypeKeys.RichText, FieldTypeKeys.Blocks, FieldTypeKeys.Media]);

        var blocks = await client.GetFromJsonAsync<FieldTypeDescriptor>(
            $"{FieldTypes}/{FieldTypeKeys.Blocks}",
            cancellationToken);

        blocks!.IsContainer.Should().BeTrue();
        blocks.Capabilities.Should().Contain(nameof(FieldTypeCapabilities.Container));
        // This document is what the P1-29 configuration form builds its controls from, so the
        // dialect and the settings both have to survive the round trip.
        blocks.ConfigurationSchema.GetProperty("$schema").GetString()
            .Should().Be("https://json-schema.org/draft/2020-12/schema");
        blocks.ConfigurationSchema.GetProperty("properties").TryGetProperty("allowedBlockTypes", out _)
            .Should().BeTrue();

        var unknown = await client.GetAsync($"{FieldTypes}/carouselOfDoom", cancellationToken);

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AViewerMayReadStructureButNotChangeIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var developer = await DeveloperAsync(cancellationToken);
        await CreateBlockTypeAsync(developer, "viewer-visible", cancellationToken);

        using var viewer = await ClientAsync(cancellationToken, CmsRoles.Viewer);

        var list = await viewer.GetAsync(BlockTypes, cancellationToken);
        var fieldTypes = await viewer.GetAsync(FieldTypes, cancellationToken);
        var create = await viewer.PostAsJsonAsync(
            BlockTypes,
            new CreateBlockTypeRequest("viewer-attempt", "Viewer attempt"),
            cancellationToken);
        var createComposition = await viewer.PostAsJsonAsync(
            Compositions,
            new CreateCompositionRequest("viewer-group", "Viewer group"),
            cancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        fieldTypes.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        createComposition.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfigurationIsCheckedOnAPropertyExactlyAsOnAZone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await DeveloperAsync(cancellationToken);
        var blockType = await CreateBlockTypeAsync(client, "config-checked", cancellationToken);

        var bad = await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/properties",
            new CreatePropertyRequest(
                "body",
                "Body",
                FieldTypeKeys.PlainText,
                Configuration("""{"maxlength":10}""")),
            cancellationToken);

        var early = await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/properties",
            new CreatePropertyRequest(
                "photo",
                "Photo",
                FieldTypeKeys.Media,
                Configuration("""{"minWidth":800}""")),
            cancellationToken);

        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(bad, cancellationToken)).Should().Contain(FieldConfigurationCodes.UnknownSetting);

        early.StatusCode.Should().Be(HttpStatusCode.Created);

        var saved = await early.Content.ReadFromJsonAsync<PropertySaveResult>(cancellationToken);

        // Nothing reported: the media picker settings were the deferred ones until P5-19 began
        // enforcing them on the publish path. The refusal above is the half this test is really
        // about — a property's configuration is checked exactly as a zone's is.
        saved!.Warnings.Should().BeEmpty();
    }

    private static JsonElement Configuration(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<HttpClient> DeveloperAsync(CancellationToken cancellationToken) =>
        await ClientAsync(cancellationToken, CmsRoles.Developer);

    private async Task<HttpClient> ClientAsync(CancellationToken cancellationToken, params string[] roles) =>
        await CmsApplicationFactory.WithAntiforgeryTokenAsync(_factory.CreateClientAs(roles), cancellationToken);

    private static async Task<BlockTypeSummary> CreateBlockTypeAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            BlockTypes,
            new CreateBlockTypeRequest(key, key),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<BlockTypeDetail>(cancellationToken))!.BlockType;
    }

    private static async Task<PropertySaveResult> AddPropertyAsync(
        HttpClient client,
        BlockTypeSummary blockType,
        string key,
        string fieldTypeKey,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            $"{BlockTypes}/{blockType.Id}/properties",
            new CreatePropertyRequest(key, key, fieldTypeKey),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<PropertySaveResult>(cancellationToken))!;
    }

    private static async Task<CompositionSummary> CreateCompositionAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            Compositions,
            new CreateCompositionRequest(key, key),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<CompositionDetail>(cancellationToken))!.Composition;
    }

    private static async Task AddCompositionPropertyAsync(
        HttpClient client,
        CompositionSummary composition,
        string key,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            $"{Compositions}/{composition.Id}/properties",
            new CreatePropertyRequest(key, key, FieldTypeKeys.PlainText),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
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
