using System.Net;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// Two renderers reading the database in one render do not collide (ADR-0022).
/// </summary>
/// <remarks>
/// Blazor starts sibling components' asynchronous lifecycle methods concurrently: each runs to its
/// first <c>await</c>, and from there they overlap. A page with a media zone and a reusable footer
/// therefore has two queries in flight at once, and while they shared the request's scoped
/// <c>ApplicationDbContext</c> the second was refused with "a second operation was started on this
/// context instance" — which delivery turned into a footer that rendered nothing on every page of
/// the site.
/// <para>
/// Asserted through an ordinary anonymous request rather than by driving the resolvers directly.
/// What broke was not either resolver but the two of them in one render, and only the real pipeline
/// puts them there in the order that collides.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class ConcurrentRenderReadTests(SqlServerFixture fixture) : IAsyncLifetime
{
    /// <summary>The template with both a <c>hero</c> and a <c>footer</c>, which is the shape that fails.</summary>
    private const string TemplateKey = "marketing-landing";

    /// <summary>No attributes: the sanitizer strips a class on the way out, and would fail the match.</summary>
    private const string FooterHtml = "<p>Shared footer</p>";

    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task AMediaZoneAndAReusableFooterBothRenderOnOnePage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var template = await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            PageWorkbench.MediaZone("hero"),
            PageWorkbench.ReusableZone("footer"));

        var image = await _bench.AddImageAsync(800, 600, cancellationToken);

        var footer = await _bench.AddReusableAsync("Footer", cancellationToken);
        var filled = await _bench.SetReusableHtmlAsync(footer, FooterHtml, cancellationToken);

        await _bench.PublishReusableAsync(filled.Summary.Id, cancellationToken);

        var page = await _bench.AddPageAsync(template, "Campaign", cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(
                Payload(page.TemplateRevision, image.Id, filled.Summary.Id),
                null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(PageWorkbench.Because(saved));

        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(PageWorkbench.Because(published));
        _bench.Context.ChangeTracker.Clear();

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/campaign", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // The hero is what holds the context open; the footer is what used to be refused it. Both
        // are asserted, because a fix that serialized them by dropping one would pass on either
        // half alone.
        html.Should().Contain("<img").And.Contain(FooterHtml);
    }

    private static string Payload(int templateRevision, int mediaId, int reusableContentId) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{TemplateKey}}",
          "templateRevision": {{templateRevision}},
          "zones": {
            "hero": { "type": "media", "mediaId": {{mediaId}}, "altOverride": null, "crop": null },
            "footer": { "type": "reusable", "reusableContentId": {{reusableContentId}},
                        "pinnedVersionId": null } } }
        """;
}
