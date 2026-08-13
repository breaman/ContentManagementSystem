using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Tests.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Tests.Fields;

/// <summary>
/// The one rule that matters in <see cref="IFieldType"/> (task P1-13, spec section 7.3): every
/// registered field type reports every entity a populated value points at.
/// </summary>
/// <remarks>
/// A field type that omits a reference produces a page that silently fails to invalidate when its
/// dependency changes. The symptom is stale content on a page nobody edited, and by the time it is
/// noticed there is nothing left to trace it back to — which is why this is a contract test over the
/// whole registry rather than a case in each field type's own file.
/// <para>
/// Adding a reference-bearing field type therefore means adding a sample here. That is deliberate
/// friction: the test fails with the name of the field type that has no sample, so the rule is
/// enforced against implementations that do not exist yet.
/// </para>
/// </remarks>
public class ReferenceExtractionContractTests
{
    private const string BlockId = "0f6c8b1e-3a4d-4f2b-9c7e-1d2a3b4c5d6e";

    private const string NestedBlockId = "7a1b2c3d-4e5f-4061-8293-a4b5c6d7e8f9";

    /// <summary>
    /// A representative populated value per field type that claims
    /// <see cref="FieldTypeCapabilities.ReferenceBearing"/>.
    /// </summary>
    private static readonly Dictionary<string, string> PopulatedValues = new(StringComparer.Ordinal)
    {
        [FieldTypeKeys.Media] = """{ "type": "media", "mediaId": 812 }""",

        [FieldTypeKeys.MediaList] = """{ "type": "mediaList", "items": [ { "mediaId": 812 } ] }""",

        [FieldTypeKeys.Link] = """{ "type": "link", "kind": "page", "pageId": 44 }""",

        [FieldTypeKeys.PageReference] = """{ "type": "pageReference", "value": 44 }""",

        [FieldTypeKeys.Reusable] = """{ "type": "reusable", "reusableContentId": 3 }""",

        [FieldTypeKeys.Blocks] =
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "properties": {
                    "image": { "type": "media", "mediaId": 812 }
                } }
            ] }
            """,
    };

    /// <summary>
    /// A value whose references sit one level further down than
    /// <see cref="PopulatedValues"/> puts them, per field type flagged
    /// <see cref="FieldTypeCapabilities.Container"/>.
    /// </summary>
    /// <remarks>
    /// The second case the S1 spike added to this task. The flat case passes for a container that
    /// reports only its own level and drops everything nested inside it — which is the failure this
    /// whole test exists to catch, one level down.
    /// </remarks>
    private static readonly Dictionary<string, string> NestedValues = new(StringComparer.Ordinal)
    {
        [FieldTypeKeys.Blocks] =
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{BlockId}}", "blockTypeKey": "grid", "properties": {
                    "rows": { "type": "blocks", "items": [
                        { "id": "{{NestedBlockId}}", "blockTypeKey": "card", "properties": {
                            "image": { "type": "media", "mediaId": 900 }
                        } }
                    ] }
                } }
            ] }
            """,
    };

    public static TheoryData<string> ReferenceBearingKeys => Keys(FieldTypeCapabilities.ReferenceBearing);

    public static TheoryData<string> ContainerKeys => Keys(FieldTypeCapabilities.Container);

    [Theory]
    [MemberData(nameof(ReferenceBearingKeys))]
    public void EveryReferenceBearingFieldTypeReportsAPopulatedValue(string key)
    {
        var fieldType = Registry().Find(key);

        fieldType.Should().NotBeNull();

        PopulatedValues.Should().ContainKey(key,
            "a field type claiming ReferenceBearing needs a representative populated value here, " +
            "or nothing checks that it reports one");

        fieldType.ExtractReferences(PopulatedValues[key]).Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ContainerKeys))]
    public void EveryContainerReportsTheReferencesOfANestedValue(string key)
    {
        var fieldType = Registry().Find(key);

        fieldType.Should().NotBeNull();

        NestedValues.Should().ContainKey(key,
            "a container needs a value with a reference nested inside it, or the flat case passes " +
            "for a container that silently drops everything below its own level");

        fieldType.ExtractReferences(NestedValues[key]).Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ReferenceBearingKeys))]
    public void ExtractionAgreesWithValidationOnWhatCountsAsAReference(string key)
    {
        var fieldType = Registry().Find(key);

        fieldType.Should().NotBeNull();

        var references = fieldType.ExtractReferences(PopulatedValues[key]);

        // Every extracted row has to name an entity an id could belong to. A row pointing at 0 or a
        // negative id is a foreign key waiting to fail, and it would fail on the publish path.
        references.Should().OnlyContain(reference => reference.TargetId > 0);
    }

    [Fact]
    public void NoFieldTypeReportsReferencesWithoutClaimingTo()
    {
        var registry = Registry();

        foreach (var fieldType in registry.All)
        {
            if (fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing)) continue;

            if (!PopulatedValues.TryGetValue(fieldType.Key, out var populated)) continue;

            // The capability is what the engine dispatches on. A field type that reports rows
            // without declaring it would have them read on some paths and ignored on others.
            fieldType.ExtractReferences(populated).Should().BeEmpty();
        }
    }

    private static TheoryData<string> Keys(FieldTypeCapabilities capability)
    {
        var data = new TheoryData<string>();

        foreach (var fieldType in Registry().All)
        {
            if (fieldType.Capabilities.HasFlag(capability)) data.Add(fieldType.Key);
        }

        return data;
    }

    /// <summary>
    /// The registry as a deployment builds it, so a field type registered by the assembly scan is
    /// covered whether or not anyone remembered to name it here.
    /// </summary>
    private static IFieldTypeRegistry Registry()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IContentSanitizer, RecordingSanitizer>()
            .AddCmsFieldTypes()
            .BuildServiceProvider();

        return provider.GetRequiredService<IFieldTypeRegistry>();
    }
}
