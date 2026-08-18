using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Configuration;
using ContentManagementSystem.Core.Tests.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Tests.Fields.Configuration;

/// <summary>
/// The JSON Schema document spec section 7.2 calls for, generated from the declared schema
/// (task P1-12).
/// </summary>
/// <remarks>
/// The document is what crosses the wire — <c>/api/cms/v1/field-types</c> serves it and the zone
/// configuration form builds its controls from it — so what matters here is that a client reading it
/// sees the same rules the server enforces, not the exact bytes.
/// </remarks>
public class FieldConfigurationSchemaWriterTests
{
    [Test]
    public void ADocumentIsWrittenForEveryRegisteredFieldType()
    {
        foreach (var fieldType in Registry().All)
        {
            var document = Write(fieldType.Key);

            document.GetProperty("$schema").GetString().Should().Be(FieldConfigurationSchemaWriter.Dialect);
            document.GetProperty("$id").GetString().Should().Be($"urn:cms:field-configuration:{fieldType.Key}");
            document.GetProperty("type").GetString().Should().Be("object");

            // The point of the exercise. A client that honours the document refuses the same
            // mistyped setting the server does.
            document.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        }
    }

    [Test]
    public void EveryDeclaredSettingAppearsWithItsType()
    {
        var properties = Write(FieldTypeKeys.PlainText).GetProperty("properties");

        properties.GetProperty("minLength").GetProperty("type").GetString().Should().Be("integer");
        properties.GetProperty("maxLength").GetProperty("minimum").GetInt32().Should().Be(1);
        properties.GetProperty("pattern").GetProperty("format").GetString().Should().Be("regex");
        properties.GetProperty("patternMessage").GetProperty("type").GetString().Should().Be("string");
    }

    [Test]
    public void AnExclusiveBoundIsWrittenAsOne()
    {
        var step = Write(FieldTypeKeys.Number).GetProperty("properties").GetProperty("step");

        step.TryGetProperty("minimum", out _).Should().BeFalse();
        step.GetProperty("exclusiveMinimum").GetDecimal().Should().Be(0);
    }

    [Test]
    public void AClosedSetOfValuesIsWrittenAsAnEnum()
    {
        var profile = Write(FieldTypeKeys.RichText).GetProperty("properties").GetProperty("profile");

        profile.GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal("basic", "extended");
    }

    [Test]
    public void AListSettingConstrainsItsItemsRatherThanItself()
    {
        var palette = Write(FieldTypeKeys.Color).GetProperty("properties").GetProperty("palette");

        palette.GetProperty("type").GetString().Should().Be("array");
        palette.GetProperty("items").GetProperty("pattern").GetString().Should().Be("^#[0-9a-fA-F]{6}$");
    }

    [Test]
    public void ARangeIsCarriedAsAnAnnotationBecauseJsonSchemaCannotSayIt()
    {
        var range = Write(FieldTypeKeys.Blocks).GetProperty("x-cmsOrderedRanges").EnumerateArray().Single();

        range.GetProperty("lower").GetString().Should().Be("min");
        range.GetProperty("upper").GetString().Should().Be("max");
    }

    [Test]
    public void ASettingNotYetEnforcedSaysSoInBothPlaces()
    {
        var deferred = Write(DeferredSettingFieldType.TypeKey)
            .GetProperty("properties")
            .GetProperty(DeferredSettingFieldType.DeferredSetting);

        // In the description for anything that only renders JSON Schema, and as an annotation for
        // the backoffice, which badges it.
        deferred.GetProperty("description").GetString().Should()
            .EndWith($"Not enforced until {DeferredSettingFieldType.Phase}.");
        deferred.GetProperty("x-cmsNotEnforcedUntil").GetString().Should()
            .Be(DeferredSettingFieldType.Phase);
    }

    [Test]
    public void AFieldTypeTakingNoConfigurationStillGetsADocument()
    {
        var document = Write(FieldTypeKeys.Boolean);

        // An empty properties object plus additionalProperties: false says "nothing is accepted",
        // which is the correct statement rather than a missing one.
        document.GetProperty("properties").EnumerateObject().Should().BeEmpty();
        document.TryGetProperty("x-cmsOrderedRanges", out _).Should().BeFalse();
    }

    private static JsonElement Write(string key)
    {
        var json = FieldConfigurationSchemaWriter.Write(Registry().Find(key)!, indented: true);

        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static IFieldTypeRegistry Registry()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IContentSanitizer, RecordingSanitizer>()
            .AddCmsFieldTypes()
            .AddCmsFieldType<DeferredSettingFieldType>()
            .BuildServiceProvider();

        return provider.GetRequiredService<IFieldTypeRegistry>();
    }
}
