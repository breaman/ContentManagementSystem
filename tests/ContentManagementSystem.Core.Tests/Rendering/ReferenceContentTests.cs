using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The two reference templates and three reference block types (tasks P3-10 and P3-23).
/// </summary>
/// <remarks>
/// Driven through <see cref="CmsPageHost"/> and the real component catalog rather than by
/// instantiating a template directly, because what is worth asserting is the composition: the host
/// resolves a stored <c>templateKey</c>, the template names zone keys and nothing else, and each
/// zone's value reaches the renderer its own <c>type</c> discriminator selects.
/// <para>
/// The claim these hold in place is the one that made the reference content worth shipping — that
/// <em>every</em> field type has a placement, and therefore that every renderer in
/// <c>Rendering/Fields</c> has somewhere it is actually run.
/// </para>
/// </remarks>
public class ReferenceContentTests : IDisposable
{
    private readonly FieldRendererHarness _harness = new();

    public ReferenceContentTests() =>
        // Every media item the reference content places, so the pages render pictures rather than
        // the section 15.3 placeholder — which is what makes the renderer's real output, and the
        // absence of any warning about it, assertable here (task P5-20).
        _harness.Media
            .Add(401, width: 2400, height: 1350)
            .Add(812, width: 1600, height: 900)
            .Add(900, width: 1200, height: 800)
            .Add(902, width: 800, height: 800);

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TheReferenceTemplatesAndBlockTypesAreDeclaredWithTheKeysContentStores()
    {
        var declarations = CmsComponentScanner.ScanTypes(
            typeof(RenderingAssemblyMarker).Assembly.GetTypes());

        // The keys, not the class names. A payload stores the key, so renaming a class is free and
        // changing one of these strings orphans every page authored against it.
        declarations.Templates.Keys.Should().BeEquivalentTo(["marketing-landing", "article"]);

        // Four, not three. The first three are the reference content of P3-10; 'rawHtml' is not a
        // sample at all — the database seeds a built-in block type under that key so reusable content
        // has a shape without a developer defining one first (spec section 9.1), and a component has
        // to declare it or that seeded row is orphaned from the moment it is inserted.
        declarations.BlockTypes.Keys.Should().BeEquivalentTo(
            ["hero-banner", "rich-text", "feature-grid", "rawHtml"]);
    }

    [Fact]
    public void TheFallbackTemplateIsNotSelectable()
    {
        var declarations = CmsComponentScanner.ScanTypes(
            typeof(RenderingAssemblyMarker).Assembly.GetTypes());

        // It renders pages whose template component is missing, so a [CmsTemplate] on it would put
        // it in the create-page picker and let the reconciler write a row an editor could choose.
        declarations.Templates.Values
            .Should().NotContain(declaration => declaration.ComponentType == typeof(CmsFallbackTemplate));
    }

    [Fact]
    public void TheReferenceSetBetweenThemPlacesEveryRegisteredFieldType()
    {
        // Compared against the registry rather than against a list restated here: a field type added
        // in a later phase fails this until the reference content gives it a home, which is exactly
        // the point of shipping reference content at all.
        var placed = ArticleZones.Values
            .Concat(LandingZones.Values)
            .Concat(HeroBannerProperties.Values)
            .Concat(RichTextProperties.Values)
            .Concat(FeatureGridProperties.Values)
            .ToHashSet(StringComparer.Ordinal);

        var unplaced = FieldRendererHarness.Registry.All
            .Select(fieldType => fieldType.Key)
            .Where(key => !placed.Contains(key))
            .ToList();

        unplaced.Should().BeEmpty(
            "every registered field type needs a zone or a block property in the reference content, " +
            "or its renderer is never exercised by anything");
    }

    [Fact]
    public void TheArticleTemplateRendersEveryValueShapedZoneThroughItsOwnRenderer()
    {
        var markup = _harness.RenderPage(ArticleContext());

        markup.Should().Contain("data-template=\"article\"");

        // One assertion per renderer, each looking for markup only that renderer emits. A value
        // reaching the wrong renderer and a renderer drawing the wrong thing look identical to a
        // test that only checks the text is somewhere on the page.
        markup.Should().Contain("<p class=\"cms-article-kicker\">Analysis</p>");
        markup.Should().Contain("First line").And.Contain("<br").And.Contain("Second line");
        markup.Should().Contain("<time class=\"cms-datetime\" datetime=\"2026-08-15T09:30:00Z\"");
        markup.Should().Contain("<time class=\"cms-date\" datetime=\"2026-09-01\"");
        markup.Should().Contain("<span class=\"cms-boolean\" data-value=\"true\">Yes</span>");
        markup.Should().Contain("<dd>wide</dd>");
        markup.Should().Contain("<picture class=\"cms-media\">").And.Contain("alt=\"A poster\"");
        markup.Should().Contain("<ul class=\"cms-media-list\">");
        markup.Should().Contain("<li class=\"cms-tag\">policy</li>");
        markup.Should().Contain("<a href=\"/pricing\"").And.Contain("Pricing");
        markup.Should().Contain("<em>emphasis</em>");
        markup.Should().Contain("Embedded");
    }

    [Fact]
    public void TheNumberZoneIsRenderedExactlyAsStored()
    {
        var markup = _harness.RenderPage(ArticleContext());

        // Emitted verbatim rather than formatted: the stored precision survives, and no page depends
        // on the culture the server happens to be running under.
        markup.Should().Contain("<dd>7.50</dd>");
    }

    [Fact]
    public void AJsonZoneRendersNothingAndIsNotReportedAsAProblem()
    {
        var markup = _harness.RenderPage(ArticleContext());

        // Developer-only data for the markup around it to read. Silent by design, and specifically
        // not logged, unlike every other condition on this page that renders nothing.
        markup.Should().NotContain("analytics-token");
        _harness.Logs.Entries.Should().BeEmpty();
    }

    [Fact]
    public void TheLandingTemplateRendersItsBlockZonesThroughTheBlockComponents()
    {
        var markup = _harness.RenderPage(LandingContext());

        markup.Should().Contain("data-template=\"marketing-landing\"");
        markup.Should().Contain("data-block-type=\"hero-banner\"");
        markup.Should().Contain("<h2 class=\"cms-hero-headline\">Save the date</h2>");

        // A block's structured properties, drawn by each field type's own renderer rather than read
        // out of the JSON by the block's markup — the path CmsBlockProperty exists for.
        markup.Should().Contain("alt=\"A stage\"").And.Contain("<source type=\"image/webp\"");
        markup.Should().Contain("<span class=\"cms-color\" data-color=\"#ff8800\">#ff8800</span>");
        markup.Should().Contain("href=\"https://example.test/tickets\"");

        // And the zone-level renderers on the same page.
        markup.Should().Contain("<strong>Introducing</strong>");
        markup.Should().Contain("data-color=\"#123456\"");
        markup.Should().Contain("href=\"https://example.test/buy\"");
    }

    [Fact]
    public void ABlockNestedInsideABlockRendersThroughTheSameDispatch()
    {
        var markup = _harness.RenderPage(LandingContext());

        // The container case: zone → blocks → feature-grid → items → rich-text. A block context
        // scoped wrongly, or a nested block's revision resolved against the outer block's schema,
        // breaks here and nowhere else.
        markup.Should().Contain("data-block-type=\"feature-grid\"");
        markup.Should().Contain("<h2 class=\"cms-feature-grid-heading\">What you get</h2>");
        markup.Should().Contain("data-block-type=\"rich-text\"");
        markup.Should().Contain("Nested prose");
    }

    [Fact]
    public void ABlockTypeNoComponentDeclaresIsSkippedAndItsSiblingsStillRender()
    {
        var payload = RenderingHarness.PayloadFor(
            "marketing-landing",
            ("hero", $$"""
                {
                  "type": "blocks",
                  "items": [
                    {{HeroBannerJson}},
                    { "id": "22222222-2222-4222-8222-222222222222", "blockTypeKey": "retired-block",
                      "blockTypeRevision": 1, "properties": {} }
                  ]
                }
                """));

        var markup = _harness.RenderPage(Context(payload, "marketing-landing"));

        // Spec section 15.3: the block is skipped and logged, and the list around it is unaffected.
        markup.Should().Contain("Save the date");
        _harness.Logs.Entries.Should().Contain(entry => entry.Message.Contains("retired-block"));
    }

    /// <summary>The zones the article reference template places, and the field type filling each.</summary>
    private static Dictionary<string, string> ArticleZones { get; } = new(StringComparer.Ordinal)
    {
        ["kicker"] = FieldTypeKeys.PlainText,
        ["standfirst"] = FieldTypeKeys.MultilineText,
        ["publishedAt"] = FieldTypeKeys.DateTime,
        ["reviewedOn"] = FieldTypeKeys.Date,
        ["readingMinutes"] = FieldTypeKeys.Number,
        ["isFeatured"] = FieldTypeKeys.Boolean,
        ["layout"] = FieldTypeKeys.Choice,
        ["poster"] = FieldTypeKeys.Media,
        ["body"] = FieldTypeKeys.Blocks,
        ["embed"] = FieldTypeKeys.Html,
        ["gallery"] = FieldTypeKeys.MediaList,
        ["tags"] = FieldTypeKeys.Tags,
        ["related"] = FieldTypeKeys.PageReference,
        ["analytics"] = FieldTypeKeys.Json,
    };

    /// <summary>The zones the landing reference template places, and the field type filling each.</summary>
    private static Dictionary<string, string> LandingZones { get; } = new(StringComparer.Ordinal)
    {
        ["hero"] = FieldTypeKeys.Blocks,
        ["intro"] = FieldTypeKeys.RichText,
        ["body"] = FieldTypeKeys.Blocks,
        ["accent"] = FieldTypeKeys.Color,
        ["cta"] = FieldTypeKeys.Link,
        ["footer"] = FieldTypeKeys.Reusable,
    };

    private static Dictionary<string, string> HeroBannerProperties { get; } = new(StringComparer.Ordinal)
    {
        ["headline"] = FieldTypeKeys.PlainText,
        ["standfirst"] = FieldTypeKeys.MultilineText,
        ["image"] = FieldTypeKeys.Media,
        ["cta"] = FieldTypeKeys.Link,
        ["background"] = FieldTypeKeys.Color,
        ["isFullBleed"] = FieldTypeKeys.Boolean,
    };

    private static Dictionary<string, string> RichTextProperties { get; } = new(StringComparer.Ordinal)
    {
        ["body"] = FieldTypeKeys.RichText,
        ["alignment"] = FieldTypeKeys.Choice,
        ["embed"] = FieldTypeKeys.Html,
        ["settings"] = FieldTypeKeys.Json,
    };

    private static Dictionary<string, string> FeatureGridProperties { get; } = new(StringComparer.Ordinal)
    {
        ["heading"] = FieldTypeKeys.PlainText,
        ["columns"] = FieldTypeKeys.Number,
        ["publishedOn"] = FieldTypeKeys.Date,
        ["updatedAt"] = FieldTypeKeys.DateTime,
        ["items"] = FieldTypeKeys.Blocks,
        ["gallery"] = FieldTypeKeys.MediaList,
        ["tags"] = FieldTypeKeys.Tags,
        ["related"] = FieldTypeKeys.PageReference,
        ["promo"] = FieldTypeKeys.Reusable,
    };

    private const string HeroBannerJson =
        """
        {
          "id": "11111111-1111-4111-8111-111111111111",
          "blockTypeKey": "hero-banner",
          "blockTypeRevision": 1,
          "properties": {
            "headline": { "type": "plainText", "value": "Save the date" },
            "standfirst": { "type": "multilineText", "value": "Doors open at seven." },
            "image": { "type": "media", "mediaId": 401, "altOverride": "A stage" },
            "cta": { "type": "link", "kind": "external", "url": "https://example.test/tickets", "text": "Tickets" },
            "background": { "type": "color", "value": "#ff8800" },
            "isFullBleed": { "type": "boolean", "value": true }
          }
        }
        """;

    private RenderContext ArticleContext()
    {
        _harness.Links.Add(88, "/pricing", "Pricing");

        var payload = RenderingHarness.PayloadFor(
            "article",
            ("kicker", """{"type":"plainText","value":"Analysis"}"""),
            ("standfirst", """{"type":"multilineText","value":"First line\nSecond line"}"""),
            ("publishedAt", """{"type":"dateTime","value":"2026-08-15T09:30:00Z"}"""),
            ("reviewedOn", """{"type":"date","value":"2026-09-01"}"""),
            ("readingMinutes", """{"type":"number","value":7.50}"""),
            ("isFeatured", """{"type":"boolean","value":true}"""),
            ("layout", """{"type":"choice","value":"wide"}"""),
            ("poster", """{"type":"media","mediaId":812,"altOverride":"A poster"}"""),
            ("body", $$"""{"type":"blocks","items":[{{RichTextBlockJson("55555555", "emphasis")}}]}"""),
            ("embed", """{"type":"html","value":"<div>Embedded</div>"}"""),
            ("gallery", """{"type":"mediaList","items":[{"mediaId":900,"altOverride":"One"}]}"""),
            ("tags", """{"type":"tags","value":["policy","budget"]}"""),
            ("related", """{"type":"pageReference","value":88}"""),
            ("analytics", """{"type":"json","value":{"token":"analytics-token"}}"""));

        return Context(payload, "article");
    }

    private RenderContext LandingContext()
    {
        _harness.Links.Add(88, "/pricing", "Pricing");

        var payload = RenderingHarness.PayloadFor(
            "marketing-landing",
            ("hero", $$"""{"type":"blocks","items":[{{HeroBannerJson}}]}"""),
            ("intro", """{"type":"richText","format":"html","value":"<p><strong>Introducing</strong> the thing.</p>"}"""),
            ("body", $$"""
                {
                  "type": "blocks",
                  "items": [
                    {
                      "id": "33333333-3333-4333-8333-333333333333",
                      "blockTypeKey": "feature-grid",
                      "blockTypeRevision": 1,
                      "properties": {
                        "heading": { "type": "plainText", "value": "What you get" },
                        "columns": { "type": "number", "value": 3 },
                        "publishedOn": { "type": "date", "value": "2026-08-01" },
                        "updatedAt": { "type": "dateTime", "value": "2026-08-14T12:00:00Z" },
                        "items": { "type": "blocks", "items": [{{RichTextBlockJson("66666666", "Nested prose")}}] },
                        "gallery": { "type": "mediaList", "items": [{ "mediaId": 902 }] },
                        "tags": { "type": "tags", "value": ["feature"] },
                        "related": { "type": "pageReference", "value": 88 },
                        "promo": { "type": "reusable", "reusableContentId": 5 }
                      }
                    }
                  ]
                }
                """),
            ("accent", """{"type":"color","value":"#123456"}"""),
            ("cta", """{"type":"link","kind":"external","url":"https://example.test/buy","text":"Buy"}"""),
            ("footer", """{"type":"reusable","reusableContentId":7}"""));

        return Context(payload, "marketing-landing");
    }

    private static string RichTextBlockJson(string idPrefix, string body) =>
        $$"""
        {
          "id": "{{idPrefix}}-4444-4444-8444-444444444444",
          "blockTypeKey": "rich-text",
          "blockTypeRevision": 1,
          "properties": {
            "body": { "type": "richText", "format": "html", "value": "<p><em>{{body}}</em></p>" },
            "alignment": { "type": "choice", "value": "left" },
            "embed": { "type": "html", "value": "<span>embedded</span>" },
            "settings": { "type": "json", "value": { "debug": true } }
          }
        }
        """;

    private static RenderContext Context(ContentPayload payload, string templateKey) =>
        new(RenderingHarness.Page(templateKey), payload, CmsRenderMode.Live, schema: null);
}
