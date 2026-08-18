using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.TestSupport;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// Removing a zone from a template leaves the content authored into it intact and reachable
/// (acceptance criterion P1 #4, spec sections 8.5 and 15.3).
/// </summary>
/// <remarks>
/// Held at <c>[~]</c> through Phase 1 because the literal criterion needs a stored payload to
/// survive the removal, and there was no page to store one until <c>P2-01</c>. Both halves are
/// asserted here, and the second is the one that is easy to get wrong: <em>reachable</em> means an
/// editor can still see the value and still save the page, not merely that the bytes are on disk.
/// <para>
/// The two states are separate cases because the schema a payload is judged against is the revision
/// it captured, not the template's current one. A draft that has not adopted the new revision is
/// still being validated against a schema in which the zone exists — so the orphan only appears once
/// the page moves forward, which is exactly when an editor would see it.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class OrphanedZoneApiTests(SqlServerFixture fixture)
{
    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Test]
    public async Task RemovingAZoneLeavesTheStoredValueWhereItWas()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var (template, zoneId, pageId) = await FilledPageAsync(client, "orphan-kept", cancellationToken);

        // 200 rather than 204: removing a zone cuts a revision, and the caller needs its number.
        (await client.DeleteAsync($"{Templates}/{template.Id}/zones/{zoneId}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var page = (await client.GetFromJsonAsync<PageDetail>($"{Pages}/{pageId}", cancellationToken))!;

        // The payload is untouched. Removing a zone definition is a change to the template, and the
        // content model is deliberately not rewritten under the pages that were authored against it.
        page.ContentJson.Should().Contain("The sidebar copy");
        page.TemplateRevision.Should().Be(
            template.CurrentRevision,
            "the draft still captures the revision it was authored against");
    }

    [Test]
    public async Task TheLeftoverValueIsReportedAsObsoleteContentOnceThePageAdoptsTheNewRevision()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var (template, zoneId, pageId) = await FilledPageAsync(client, "orphan-warned", cancellationToken);

        await client.DeleteAsync($"{Templates}/{template.Id}/zones/{zoneId}", cancellationToken);

        var current = (await client.GetFromJsonAsync<TemplateDetail>(
            $"{Templates}/{template.Id}",
            cancellationToken))!.Template.CurrentRevision;

        current.Should().Be(template.CurrentRevision + 1, "removing a zone changes how content is read");

        var page = (await client.GetFromJsonAsync<PageDetail>($"{Pages}/{pageId}", cancellationToken))!;

        // The same payload, re-declared against the revision the template is on now — which is how
        // an editor adopts a structural change (spec section 8.5).
        var saved = await SaveDraftAsync(
            client,
            pageId,
            Payload(template.Key, current, "The sidebar copy"),
            page.RowVersion,
            cancellationToken);

        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(cancellationToken));

        var result = (await saved.Content.ReadFromJsonAsync<DraftSaveResult>(cancellationToken))!;

        // A warning, never an error. Erroring would strand the page: the value cannot be removed
        // without saving, and the save is what would be refused (spec section 15.3).
        result.Warnings.Should().Contain(warning =>
            warning.Code == ContentValidationCodes.ZoneOrphaned &&
            warning.Property != null && warning.Property.Contains("sidebar", StringComparison.Ordinal));

        // And it is still there afterwards. "Kept and shown as obsolete content until an editor
        // discards it" is what the warning promises, and a save that quietly dropped it would make
        // the promise false at the first thing an editor does.
        var after = (await client.GetFromJsonAsync<PageDetail>($"{Pages}/{pageId}", cancellationToken))!;

        after.ContentJson.Should().Contain("The sidebar copy");
        after.TemplateRevision.Should().Be(current);
    }

    [Test]
    public async Task APageCarryingAnOrphanedZoneCanStillBePublished()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var (template, zoneId, pageId) = await FilledPageAsync(client, "orphan-publish", cancellationToken);

        await client.DeleteAsync($"{Templates}/{template.Id}/zones/{zoneId}", cancellationToken);

        var validation = await client.PostAsync($"{Pages}/{pageId}/validate", null, cancellationToken);
        var checks = (await validation.Content.ReadFromJsonAsync<PublishValidation>(cancellationToken))!;

        checks.CanPublish.Should().BeTrue("obsolete content is a warning, and warnings do not block");

        // Acknowledged rather than suppressed: the publish still has to be a decision somebody made
        // with the warnings in front of them (spec section 22.2).
        var published = await client.PostAsJsonAsync(
            $"{Pages}/{pageId}/publish",
            new PublishPageRequest(true),
            cancellationToken);

        published.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Creates a template with a second zone, a page on it, and content in both zones.
    /// </summary>
    /// <returns>The template as it stood, the removable zone's id, and the page's id.</returns>
    private static async Task<(TemplateSummary Template, int ZoneId, int PageId)> FilledPageAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken)
    {
        var template = await CreateTemplateAsync(client, key, cancellationToken);

        var zone = await client.PostAsJsonAsync(
            $"{Templates}/{template.Id}/zones",
            new CreateZoneRequest("sidebar", "Sidebar", FieldTypeKeys.PlainText),
            cancellationToken);

        zone.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await zone.Content.ReadFromJsonAsync<ZoneSaveResult>(cancellationToken))!;
        var revision = template.CurrentRevision + 1;
        var page = await CreatePageAsync(
            client,
            template with { CurrentRevision = revision },
            "Handbook",
            cancellationToken);

        var saved = await SaveDraftAsync(
            client,
            page.Summary.Id,
            Payload(template.Key, revision, "The sidebar copy"),
            page.RowVersion,
            cancellationToken);

        saved.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await saved.Content.ReadAsStringAsync(cancellationToken));

        return (template with { CurrentRevision = revision }, created.Zone.Id, page.Summary.Id);
    }

    /// <summary>A payload filling the template's own zone and the one that will be removed.</summary>
    private static string Payload(string templateKey, int templateRevision, string sidebar) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = ContentPayload.CurrentSchemaVersion,
            templateKey,
            templateRevision,
            zones = new Dictionary<string, object>
            {
                ["body"] = new { type = FieldTypeKeys.PlainText, value = "The body copy" },
                ["sidebar"] = new { type = FieldTypeKeys.PlainText, value = sidebar },
            },
        });
}
