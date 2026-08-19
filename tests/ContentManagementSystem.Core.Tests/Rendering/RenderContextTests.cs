using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Rendering;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The render context and the cache tags it accumulates (task P3-08, spec sections 15.2 and 16.2).
/// </summary>
/// <remarks>
/// Invalidation is derived from what was actually rendered. That makes the tag strings themselves a
/// contract between two sides that ship phases apart — the renderer that adds one and the publish
/// that evicts by it — so the formats are pinned here rather than left to agree by convention.
/// </remarks>
public class RenderContextTests
{
    [Test]
    public void EveryRenderStartsTaggedWithItsOwnPageAndTemplate()
    {
        // Seeded by the context rather than by each caller: a tag that has to be remembered is one
        // that gets forgotten on a code path nobody revisits, leaving a stale page live.
        var context = RenderingHarness.Context(RenderingHarness.Payload());

        context.CacheTags.ToArray().Should().Equal("page:44", "tpl:7");
    }

    [Test]
    public void TagsAccumulateDuringTheRenderAndAreCollectedOnce()
    {
        var context = RenderingHarness.Context(RenderingHarness.Payload());

        context.CacheTags.AddMedia(812);
        context.CacheTags.AddReusable(3);
        context.CacheTags.AddMedia(812);

        context.CacheTags.ToArray().Should().Equal("media:812", "page:44", "ru:3", "tpl:7");
        context.CacheTags.Count.Should().Be(4);
    }

    [Test]
    public void TheTagFormatsAreTheOnesTheSpecTableNames()
    {
        // Spec section 16.2. Eviction on publish spells these independently, so a change here is a
        // change to a contract, not to a detail.
        CacheTags.Page(44).Should().Be("page:44");
        CacheTags.Reusable(3).Should().Be("ru:3");
        CacheTags.Media(812).Should().Be("media:812");
        CacheTags.Template(7).Should().Be("tpl:7");
        CacheTags.Navigation("main").Should().Be("nav:main");
        CacheTags.All.Should().Be("content");
    }

    [Test]
    public void TwoContextsDoNotShareATagSet()
    {
        // The failure this prevents is one visitor's dependencies being applied to another's
        // response — a page evicted by something it never rendered, or worse, not evicted by
        // something it did.
        var first = RenderingHarness.Context(RenderingHarness.Payload());
        var second = RenderingHarness.Context(RenderingHarness.Payload());

        first.CacheTags.AddMedia(812);

        second.CacheTags.Contains("media:812").Should().BeFalse();
    }

    [Test]
    [Arguments(CmsRenderMode.Live, false)]
    [Arguments(CmsRenderMode.Preview, true)]
    [Arguments(CmsRenderMode.ScheduledPreview, true)]
    public void PreviewIsBothPreviewModes(CmsRenderMode mode, bool expected)
    {
        // Renderers branch on this to decide whether an unpublished link target resolves and is
        // badged or degrades to plain text, so a scheduled preview that read as Live would show an
        // editor a broken link that is not actually broken.
        var context = RenderingHarness.Context(RenderingHarness.Payload(), mode: mode);

        context.IsPreview.Should().Be(expected);
    }
}
