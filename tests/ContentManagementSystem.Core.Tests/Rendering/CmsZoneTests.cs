using Bunit;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Tests.Content;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Content;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The zone component: the indirection the content model rests on (task P3-08, spec section 15.2).
/// </summary>
/// <remarks>
/// Most of these are about what happens when the content and the deployment disagree, because that
/// is where a delivery pipeline either logs a non-event or takes a public page down. Spec section
/// 15.3 permits exactly one outcome for all of them: render nothing, log, never throw.
/// </remarks>
public class CmsZoneTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly RecordingLoggerProvider _logs = new();

    public CmsZoneTests()
    {
        _bunit.Services.AddLogging(logging => logging.AddProvider(_logs));
        _bunit.Services.AddSingleton<IFieldRendererCatalog>(new TestFieldRendererCatalog(
            ("plainText", typeof(RecordingFieldRenderer)),
            ("richText", typeof(AlternateFieldRenderer))));
    }

    public void Dispose()
    {
        _bunit.Dispose();
        _logs.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void AZoneRendersItsFieldTypesRendererWithTheStoredValue()
    {
        var markup = RenderZone(RenderingHarness.Payload(
            ("hero", """{"type":"plainText","value":"Hello"}""")));

        markup.Should().Contain("data-renderer=\"recording\"")
            .And.Contain("data-property=\"hero\"")
            .And.Contain("Hello");
    }

    [Test]
    public void AZoneTheTemplateNamesButThePayloadHasNeverHeldRendersNothing()
    {
        // This is what makes "adding a zone is free" (spec section 8.5) true at render time as well
        // as at validation time: every page already published predates the new zone.
        RenderZone(RenderingHarness.Payload()).Should().BeEmpty();

        _logs.Entries.Should().BeEmpty("an unauthored zone is ordinary, not a fault worth logging");
    }

    [Test]
    public void AZoneAnEditorClearedRendersNothing()
    {
        var payload = new ContentPayloadBuilder(RenderingHarness.TemplateKey, 1)
            .ClearZone("hero")
            .Build();

        RenderZone(payload).Should().BeEmpty();
    }

    [Test]
    public void AnUnknownFieldTypeKeyRendersNothingAndLogsAWarning()
    {
        // Acceptance criterion P3 #9. The field type was removed from the deployment while content
        // authored against it is still live; erroring would take the whole page with it.
        var markup = RenderZone(RenderingHarness.Payload(
            ("hero", """{"type":"retiredFieldType","value":"Hello"}""")));

        markup.Should().BeEmpty();
        _logs.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("retiredFieldType"));
    }

    [Test]
    public void AStoredValueWithNoTypeDiscriminatorRendersNothingAndLogsAWarning()
    {
        // Nothing this build wrote looks like this, which is exactly why it is logged: it means
        // something else wrote the payload.
        var markup = RenderZone(RenderingHarness.Payload(("hero", """{"value":"Hello"}""")));

        markup.Should().BeEmpty();
        _logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
    }

    [Test]
    public void TheRendererIsChosenByTheStoredDiscriminatorRatherThanBySchema()
    {
        // A value has to be read by whatever wrote it. The schema here says the zone is plain text;
        // the stored bytes are rich text, because the zone's field type was changed under content
        // that already existed. Dispatching on the schema would hand rich text to a renderer that
        // cannot read it.
        var markup = RenderZone(
            RenderingHarness.Payload(("hero", """{"type":"richText","format":"html","value":"<p>Hi</p>"}""")),
            Schema("hero", "plainText"));

        markup.Should().Contain("data-renderer=\"alternate\"");
    }

    [Test]
    public void TheCapturedConfigurationReachesTheRenderer()
    {
        var markup = RenderZone(
            RenderingHarness.Payload(("hero", """{"type":"plainText","value":"Hello"}""")),
            Schema("hero", "plainText", """{"maxLength":120}"""));

        markup.Should().Contain("data-max-length=\"120\"");
    }

    [Test]
    public void ConfigurationBelongingToADifferentFieldTypeIsWithheld()
    {
        // Handing over settings meant for another field type is worse than handing over none: they
        // parse, and the value renders under bounds and formats nobody chose for it.
        var markup = RenderZone(
            RenderingHarness.Payload(("hero", """{"type":"plainText","value":"Hello"}""")),
            Schema("hero", "number", """{"maxLength":120}"""));

        markup.Should().Contain("data-max-length=\"-\"");
    }

    [Test]
    public void AZoneWithNoCapturedSchemaStillRenders()
    {
        // A template revision the database no longer holds must not blank the page: the payload
        // carries everything needed to read itself.
        var markup = RenderZone(RenderingHarness.Payload(
            ("hero", """{"type":"plainText","value":"Hello"}""")));

        markup.Should().Contain("Hello");
    }

    private static ContentSchema Schema(string zoneKey, string fieldTypeKey, string? configurationJson = null) =>
        ContentEngineHarness.Template(
            RenderingHarness.TemplateKey,
            1,
            ContentPropertySchema.Create(zoneKey, zoneKey, fieldTypeKey, configurationJson));

    private string RenderZone(ContentPayload payload, ContentSchema? schema = null) =>
        _bunit.Render<CmsZone>(parameters => parameters
                .Add(zone => zone.Name, "hero")
                .AddCascadingValue(RenderingHarness.Context(payload, schema)))
            .Markup;
}
