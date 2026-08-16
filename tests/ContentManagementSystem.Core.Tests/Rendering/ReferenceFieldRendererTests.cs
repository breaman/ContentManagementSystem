using ContentManagementSystem.Rendering;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The renderers that point at something outside the payload: links, page references, media, and
/// reusable content (task P3-09, spec sections 7.1 and 15.3).
/// </summary>
/// <remarks>
/// These four are where late binding and cache tags live, so most of what is asserted here is not
/// markup at all. A rendered dependency that was not tagged produces a page that never invalidates,
/// and that failure is invisible in the HTML — which is exactly why it needs its own assertions.
/// </remarks>
public class ReferenceFieldRendererTests : IDisposable
{
    private readonly FieldRendererHarness _harness = new();

    public ReferenceFieldRendererTests() =>
        _harness.Links
            .Add(44, "/about", "About us")
            .Add(45, "/news/launch", "Launch day")
            .Add(46, "/drafts/unreleased", "Unreleased section", isPublished: false);

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AnInternalLinkResolvesToTheTargetsCurrentUrl()
    {
        // Decision D6's whole payoff: the payload holds an id, and the URL is looked up now.
        var markup = _harness.Render("""{"type":"link","kind":"page","pageId":44,"text":"Get started"}""");

        markup.Should().Contain("href=\"/about\"").And.Contain("Get started");
    }

    [Fact]
    public void AResolvedInternalLinkTagsTheTargetPage()
    {
        // Without this, renaming the target leaves its old URL cached on every page linking to it.
        var context = RenderTagged("""{"type":"link","kind":"page","pageId":44}""");

        context.CacheTags.Contains(CacheTags.Page(44)).Should().BeTrue();
    }

    [Fact]
    public void AnInternalLinkWithNoTextFallsBackToTheTargetsCurrentTitle()
    {
        // The current title, not the one it had when the link was authored.
        _harness.Render("""{"type":"link","kind":"page","pageId":44}""").Should().Contain("About us");
    }

    [Fact]
    public void AnUnpublishedTargetIsInvisibleToTheLiveSiteAndBadgedInPreview()
    {
        // The central promise of spec section 12.3, applied to one link.
        const string zone = """{"type":"link","kind":"page","pageId":46,"text":"Coming soon"}""";

        var live = _harness.Render(zone);
        var preview = _harness.Render(zone, mode: CmsRenderMode.Preview);

        live.Should().Contain("Coming soon").And.NotContain("href");
        preview.Should().Contain("href=\"/drafts/unreleased\"").And.Contain("cms-link-draft");
    }

    [Fact]
    public void ADraftTargetIsBadgedWithVisibleTextRatherThanOnlyAClass()
    {
        // Task P3-20. The framed page is styled by the *site's* stylesheet, written by whoever built
        // the site and knowing nothing about the CMS's class names — so a badge that were only a
        // class would be invisible on every deployment nobody had told about it, which is the one
        // place spec section 12.3's "clearly badged" cannot be left optional.
        var preview = _harness.Render(
            """{"type":"link","kind":"page","pageId":46,"text":"Coming soon"}""",
            mode: CmsRenderMode.Preview);

        preview.Should().Contain("cms-draft-badge").And.Contain(">draft<");
    }

    [Fact]
    public void APublishedTargetIsNotBadgedInPreview()
    {
        // The other half, and the one that fails silently: a badge on everything says nothing, and a
        // reviewer who saw it on every link would stop reading it within a page.
        var preview = _harness.Render(
            """{"type":"link","kind":"page","pageId":44,"text":"Get started"}""",
            mode: CmsRenderMode.Preview);

        preview.Should().Contain("href=\"/about\"")
            .And.NotContain("cms-draft-badge")
            .And.NotContain("cms-link-draft");
    }

    [Fact]
    public void APageReferenceListBadgesOnlyItsUnpublishedEntries()
    {
        // The same rule one level up. A list is where the mistake actually gets made: a reviewer
        // reads six related articles and has no way to tell which two are not live yet.
        var markup = _harness.Render(
            """{"type":"pageReference","value":[44,46]}""",
            mode: CmsRenderMode.Preview);

        markup.Should().Contain("/about").And.Contain("/drafts/unreleased");

        System.Text.RegularExpressions.Regex.Matches(markup, "cms-draft-badge")
            .Should().HaveCount(1, "only the unpublished entry is a draft");
    }

    [Fact]
    public void ALinkToAPageThatNoLongerExistsRendersItsTextAndLogs()
    {
        // A dead href puts a 404 in front of a reader; rendering nothing removes a sentence's words.
        var markup = _harness.Render("""{"type":"link","kind":"page","pageId":999,"text":"Read more"}""");

        markup.Should().Contain("Read more").And.NotContain("href");
        _harness.Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void AnExternalLinkIsCheckedAgainstTheSchemeAllowlistOnTheWayOut()
    {
        // Rows written before the write-time check existed are not covered by it, and
        // 'javascript:' in an href is stored XSS (ADR 0008).
        var hostile = _harness.Render(
            """{"type":"link","kind":"external","url":"javascript:alert(1)","text":"Click"}""");

        hostile.Should().Contain("Click").And.NotContain("javascript:");

        _harness.Render("""{"type":"link","kind":"external","url":"https://example.test","text":"Docs"}""")
            .Should().Contain("href=\"https://example.test\"");
    }

    [Fact]
    public void AnExternalLinkCarriesNoopenerAndKeepsWhateverTheAuthorStored()
    {
        var markup = _harness.Render(
            """{"type":"link","kind":"external","url":"https://example.test","rel":"nofollow","text":"Docs"}""");

        markup.Should().Contain("nofollow").And.Contain("noopener").And.Contain("noreferrer");
    }

    [Fact]
    public void ATargetTheStoredValueInventedIsDropped()
    {
        var markup = _harness.Render(
            """{"type":"link","kind":"external","url":"https://example.test","target":"_everywhere"}""");

        markup.Should().NotContain("_everywhere");
    }

    [Fact]
    public void AnchorAndEmailLinksAreBuiltFromTheirOwnMembers()
    {
        _harness.Render("""{"type":"link","kind":"anchor","anchor":"#pricing","text":"Pricing"}""")
            .Should().Contain("href=\"#pricing\"");

        _harness.Render("""{"type":"link","kind":"email","email":"hi@example.test"}""")
            .Should().Contain("href=\"mailto:hi@example.test\"");
    }

    [Fact]
    public void AMediaLinkDeclaresItsDependencyEvenThoughTheLibraryIsNotBuiltYet()
    {
        // The tag has to be on every page that ever rendered the link, or the pages published before
        // P5 are invisible to invalidation forever.
        var context = RenderTagged("""{"type":"link","kind":"media","mediaId":812,"text":"Brochure"}""");

        context.CacheTags.Contains(CacheTags.Media(812)).Should().BeTrue();
    }

    [Fact]
    public void ASinglePageReferenceIsABareAnchorAndSeveralAreAList()
    {
        // A "related article" placed in a sentence must not drag a <ul> into it.
        _harness.Render("""{"type":"pageReference","value":44}""")
            .Should().Contain("href=\"/about\"").And.NotContain("<ul");

        _harness.Render("""{"type":"pageReference","value":[44,45]}""")
            .Should().Contain("<ul").And.Contain("About us").And.Contain("Launch day");
    }

    [Fact]
    public void APageReferenceThatResolvesToNothingIsOmittedAndTheRestSurvive()
    {
        var markup = _harness.Render("""{"type":"pageReference","value":[44,999,45]}""");

        markup.Should().Contain("About us").And.Contain("Launch day");
        _harness.Logs.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("999"));
    }

    [Fact]
    public void EveryReferencedPageIsTaggedIncludingTheOnesThatDidNotResolve()
    {
        // A reference to a page that is not published yet must re-render when it is, so the tag
        // cannot be conditional on success.
        var context = RenderTagged("""{"type":"pageReference","value":[44,999]}""");

        context.CacheTags.Contains(CacheTags.Page(44)).Should().BeTrue();
        context.CacheTags.Contains(CacheTags.Page(999)).Should().BeTrue();
    }

    [Fact]
    public void AnUnresolvableMediaItemRendersThePlaceholderWithItsAltTextAndTagsTheItem()
    {
        // Spec section 15.3's answer for an item that has been deleted out from under a page: the
        // placement's own words, never a broken <img>.
        var context = RenderTagged(
            """{"type":"media","mediaId":812,"altOverride":"A quiet street"}""",
            out var markup);

        markup.Should().Contain("A quiet street").And.Contain("data-media-id=\"812\"");
        context.CacheTags.Contains(CacheTags.Media(812)).Should().BeTrue();
    }

    [Fact]
    public void AMediaListTagsEveryItemItRenders()
    {
        var context = RenderTagged(
            """{"type":"mediaList","items":[{"mediaId":812},{"mediaId":813}]}""");

        context.CacheTags.Contains(CacheTags.Media(812)).Should().BeTrue();
        context.CacheTags.Contains(CacheTags.Media(813)).Should().BeTrue();
    }

    [Fact]
    public void AnUnresolvableReusablePlacementRendersNothingButStillDeclaresTheDependency()
    {
        // Nothing is registered in the resolver, so item 3 does not exist — which renders nothing
        // and logs, per spec section 15.3. What must survive that is the tag: a page that rendered
        // nothing because the item was missing has to be evicted when the item arrives, and a tag
        // added only on a successful resolve would leave it stale forever.
        var context = RenderTagged("""{"type":"reusable","reusableContentId":3}""", out var markup);

        markup.Should().BeEmpty();
        context.CacheTags.Contains(CacheTags.Reusable(3)).Should().BeTrue();
        _harness.Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    private RenderContext RenderTagged(string zoneJson) => RenderTagged(zoneJson, out _);

    private RenderContext RenderTagged(string zoneJson, out string markup)
    {
        var context = RenderingHarness.Context(RenderingHarness.Payload(("hero", zoneJson)));

        markup = _harness.RenderIn(context, "hero");

        return context;
    }
}
