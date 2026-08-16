using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The markup preview endpoint (task P6-09, acceptance criteria P6 #2 and P6 #3).
/// </summary>
/// <remarks>
/// <strong>The load-bearing assertion here is that this endpoint and the delivery path produce the
/// same bytes.</strong> The editor's preview exists to predict what publishing will show, and the
/// only way it can is by calling the same Markdig configuration and the same sanitizer — which is
/// exactly what a second implementation in the browser would have quietly stopped doing on the first
/// upgrade of either.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class MarkupPreviewApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    /// <summary>Route of the preview renderer.</summary>
    private const string Preview = $"{CmsApiEndpoints.BasePath}/markup-preview";

    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task MarkdownIsRenderedThroughThePipelineTheSiteUses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var result = await RenderAsync(
            client,
            new MarkupPreviewRequest(MarkupFormats.Markdown, "A **bold** claim."),
            cancellationToken);

        result.Html.Should().Contain("<strong>bold</strong>");
        result.RemovedAnything.Should().BeFalse();
    }

    [Fact]
    public async Task ThePreviewSaysWhatTheProfileWillTakeOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var result = await RenderAsync(
            client,
            new MarkupPreviewRequest(MarkupFormats.Html, """<p>Hi</p><script>alert(1)</script>"""),
            cancellationToken);

        // The other half of a preview, and the whole of acceptance criterion P6 #3: an author finds
        // out here that what they pasted will not survive, rather than from the published page.
        result.Html.Should().NotContain("script");
        result.Removals.Should().Contain(removal => removal.Name == "script");
    }

    [Fact]
    public async Task TheBasicProfileIsWhatAnUnaskedForOneMeans()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var result = await RenderAsync(
            client,
            new MarkupPreviewRequest(MarkupFormats.Html, """<table><tr><td>a</td></tr></table>"""),
            cancellationToken);

        // Tables are the Extended profile's; asking for nothing gets the most restrictive one, which
        // is the direction a sanitization default has to fail in.
        result.Html.Should().NotContain("<table");
    }

    [Fact]
    public async Task AMistypedProfileIsRefusedRatherThanQuietlyReplaced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var response = await client.PostAsJsonAsync(
            Preview,
            new MarkupPreviewRequest(MarkupFormats.Markdown, "Hello", "extendeed"),
            cancellationToken);

        // Falling back would show an author a preview stripped harder than their zone will be, and
        // send them chasing a problem they do not have.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AFormatWithNoReadableMeaningIsRefusedRatherThanGuessedAt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var response = await client.PostAsJsonAsync(
            Preview,
            new MarkupPreviewRequest("asciidoc", "= Heading"),
            cancellationToken);

        // Markdown rendered as HTML shows its source and HTML rendered as markdown escapes its
        // markup; neither degrades into anything an author would recognise as a preview.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task TheDeveloperProfileNeedsTheRoleThatCanAuthorAgainstIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var author = await PageApiClient.ClientAsync(_factory, cancellationToken, CmsRoles.Author);

        var response = await author.PostAsJsonAsync(
            Preview,
            new MarkupPreviewRequest(MarkupFormats.Html, "<iframe src=\"https://example.com\"></iframe>", "Developer"),
            cancellationToken);

        // It permits iframes and data attributes and is reachable only from the html field type,
        // which is itself DeveloperOnly. Granting it to anyone who asked would be a way to have the
        // server render markup the caller could not have authored.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnAnonymousCallerCannotRenderAnything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Preview,
            new MarkupPreviewRequest(MarkupFormats.Markdown, "Hello"),
            cancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheProfilesEachListWhatTheyKeep()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await PageApiClient.AdministratorAsync(_factory, cancellationToken);

        var profiles = await client.GetFromJsonAsync<List<SanitizationProfileDescriptor>>(
            $"{Preview}/profiles",
            cancellationToken);

        profiles.Should().HaveCount(3);

        // The banner the HTML editor draws is built from this, rather than from a second copy of the
        // allowlist in the browser that would eventually lie about what survives a save (P6-13).
        var basic = profiles!.Single(profile => profile.Profile == nameof(SanitizationProfile.Basic));
        var developer = profiles!.Single(profile => profile.Profile == nameof(SanitizationProfile.Developer));

        basic.Tags.Should().Contain("p").And.NotContain("iframe");
        developer.Tags.Should().Contain("iframe");

        // The profiles nest: every rule Basic enforces, the wider ones enforce as well.
        basic.Tags.Should().BeSubsetOf(developer.Tags);
    }

    private static async Task<MarkupPreviewResult> RenderAsync(
        HttpClient client,
        MarkupPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(Preview, request, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<MarkupPreviewResult>(cancellationToken))!;
    }
}
