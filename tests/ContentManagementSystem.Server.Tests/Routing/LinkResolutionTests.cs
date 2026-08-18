using System.Text.Json;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Routing;

/// <summary>
/// Internal links stored as identity and resolved late (task P3-07, decision D6).
/// </summary>
/// <remarks>
/// The point of storing a page id rather than a URL is only visible over time: a link authored
/// before a target moved still points at the right place afterwards, and nothing had to rewrite the
/// payload. That is acceptance criterion P3 #7, and it is what these assert.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class LinkResolutionTests(SqlServerFixture fixture)
{
    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task AStoredPageIdResolvesToThatPagesCurrentUrlAfterItHasMovedTwice()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var target = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(target.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        var pages = _bench.Resolve<Core.Content.IPageService>();

        foreach (var slug in new[] { "plans", "cost" })
        {
            (await pages.PatchMetadataAsync(
                target.Summary.Id,
                new PatchPageMetadataRequest { Slug = new Shared.Contracts.Api.Patch<string>(slug) },
                null,
                cancellationToken)).IsSuccess.Should().BeTrue();

            _bench.Context.ChangeTracker.Clear();
        }

        var resolved = await _bench.Resolve<ILinkResolver>()
            .ResolveAsync([target.Summary.Id], cancellationToken: cancellationToken);

        // Nothing rewrote the payload. The id was always the stored value, and the URL is derived
        // from the route table at the moment of the render.
        resolved[target.Summary.Id].Url.Should().Be("/cost");
        resolved[target.Summary.Id].IsPublished.Should().BeTrue();
        resolved[target.Summary.Id].Title.Should().Be("Pricing");
    }

    [Test]
    public async Task AnUnpublishedTargetResolvesToNothingPubliclyAndToItsDraftInPreview()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var draft = await _bench.AddPageAsync(template, "Unreleased", cancellationToken);

        var links = _bench.Resolve<ILinkResolver>();

        var publicly = await links.ResolveAsync([draft.Summary.Id], cancellationToken: cancellationToken);

        // A draft URL must never reach an anonymous visitor. The link degrades to text instead
        // (spec section 15.3).
        publicly[draft.Summary.Id].Url.Should().BeNull();
        publicly[draft.Summary.Id].IsPublished.Should().BeFalse();

        var inPreview = await links.ResolveAsync(
            [draft.Summary.Id],
            includeUnpublished: true,
            cancellationToken);

        // Inside preview the same target resolves, so a reviewer can walk an unreleased section
        // (spec section 12.3). The flag is what the badge is drawn from.
        inPreview[draft.Summary.Id].Url.Should().Be("/unreleased");
        inPreview[draft.Summary.Id].IsPublished.Should().BeFalse();
    }

    [Test]
    public async Task AnIdNamingNoPageIsAbsentFromTheResultRatherThanAnError()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var resolved = await _bench.Resolve<ILinkResolver>()
            .ResolveAsync([4242], cancellationToken: cancellationToken);

        // Delivery renders such a link as plain text and logs. Throwing would take a whole page down
        // because one card points at something somebody deleted.
        resolved.Should().BeEmpty();
    }

    [Test]
    public async Task ALinkOfAKindThePropertyForbidsIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var zone = new Zone
        {
            Key = "cta",
            Name = "Call to action",
            FieldTypeKey = FieldTypeKeys.Link,
            ConfigurationJson = """{"allowedKinds":["page"]}""",
        };

        var template = await _bench.AddTemplateAsync("landing", cancellationToken, zone);
        var page = await _bench.AddPageAsync(template, "Home", cancellationToken);

        var refused = await SaveZoneAsync(
            page.Summary.Id,
            template,
            "cta",
            """{"type":"link","kind":"external","url":"https://example.test/"}""",
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Invalid);
        refused.Diagnostics.Diagnostics.Should()
            .Contain(diagnostic => diagnostic.Code == FieldValidationCodes.LinkKind);
    }

    [Test]
    public async Task APageReferenceToATemplateThePropertyForbidsBlocksThePublish()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var zone = new Zone
        {
            Key = "featured",
            Name = "Featured article",
            FieldTypeKey = FieldTypeKeys.PageReference,
            ConfigurationJson = """{"allowedTemplates":["news-story"]}""",
        };

        var landing = await _bench.AddTemplateAsync("landing", cancellationToken, zone);
        var other = await _bench.AddTemplateAsync("microsite", cancellationToken);

        var home = await _bench.AddPageAsync(landing, "Home", cancellationToken);
        var wrongShape = await _bench.AddPageAsync(other, "Campaign", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(wrongShape.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        var saved = await SaveZoneAsync(
            home.Summary.Id,
            landing,
            "featured",
            $$"""{"type":"pageReference","value":{{wrongShape.Summary.Id}}}""",
            cancellationToken);

        // The draft saves: the restriction needs the database, so it is a publish check rather than
        // a field-type rule, and an editor must still be able to store work in progress.
        saved.IsSuccess.Should().BeTrue(Because(saved));

        _bench.Context.ChangeTracker.Clear();

        var validated = await _bench.Resolve<IPublishingService>()
            .ValidateAsync(home.Summary.Id, cancellationToken);

        validated.Value!.CanPublish.Should().BeFalse();
        validated.Value.Errors.Should()
            .Contain(diagnostic => diagnostic.Code == FieldValidationCodes.NotAllowed);
    }

    [Test]
    public async Task APageReferenceToAnAllowedTemplatePublishes()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var zone = new Zone
        {
            Key = "featured",
            Name = "Featured article",
            FieldTypeKey = FieldTypeKeys.PageReference,
            ConfigurationJson = """{"allowedTemplates":["news-story"]}""",
        };

        var landing = await _bench.AddTemplateAsync("landing", cancellationToken, zone);
        var article = await _bench.AddTemplateAsync("news-story", cancellationToken);

        var home = await _bench.AddPageAsync(landing, "Home", cancellationToken);
        var story = await _bench.AddPageAsync(article, "A Story", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(story.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        await SaveZoneAsync(
            home.Summary.Id,
            landing,
            "featured",
            $$"""{"type":"pageReference","value":{{story.Summary.Id}}}""",
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(home.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
    }

    /// <summary>Writes one zone into a page's draft through the real draft service.</summary>
    private async Task<CmsResult<DraftSaveResult>> SaveZoneAsync(
        int pageId,
        Template template,
        string zoneKey,
        string valueJson,
        CancellationToken cancellationToken)
    {
        var payload = $$"""
            {
              "schemaVersion": {{ContentPayload.CurrentSchemaVersion}},
              "templateKey": {{JsonSerializer.Serialize(template.Key)}},
              "templateRevision": {{template.CurrentRevision}},
              "zones": { {{JsonSerializer.Serialize(zoneKey)}}: {{valueJson}} }
            }
            """;

        return await _bench.Resolve<Core.Content.IDraftService>().SaveAsync(
            pageId,
            new SaveDraftRequest(payload, null),
            cancellationToken);
    }

    /// <summary>Renders a refusal's diagnostics into an assertion message.</summary>
    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
}
