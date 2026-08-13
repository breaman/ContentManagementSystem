using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Configuration;
using ContentManagementSystem.Core.Tests.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Tests.Fields.Configuration;

/// <summary>
/// The two halves of a field type's configuration contract, checked against each other rather than
/// kept in step by hand (task P1-12, spec section 7.2).
/// </summary>
/// <remarks>
/// A configuration schema is closed, so the two halves fail in opposite directions and both
/// silently. A setting the field type reads but does not declare can never be stored, so the rule
/// it implements is dead code. A setting it declares but cannot satisfy is a configuration a
/// developer is invited to write and then refused. Neither shows up in a test of either half alone.
/// <para>
/// Driven off the registry a real deployment builds, so a field type registered by the assembly
/// scan is covered whether or not anyone remembers this file — the same arrangement as
/// <see cref="ReferenceExtractionContractTests"/>.
/// </para>
/// </remarks>
public class FieldConfigurationContractTests
{
    private const string BlockId = "0f6c8b1e-3a4d-4f2b-9c7e-1d2a3b4c5d6e";

    /// <summary>
    /// Populated values per field type, enough of them to reach every branch that reads
    /// configuration.
    /// </summary>
    /// <remarks>
    /// More than one where a field type reads different settings depending on the shape stored —
    /// <c>choice</c> and <c>pageReference</c> only consult their count settings when the value is a
    /// list. A field type with no sample here fails by name rather than passing vacuously.
    /// </remarks>
    private static readonly Dictionary<string, string[]> PopulatedValues = new(StringComparer.Ordinal)
    {
        [FieldTypeKeys.PlainText] = ["""{ "type": "plainText", "value": "Hello" }"""],

        [FieldTypeKeys.MultilineText] = ["""{ "type": "multilineText", "value": "Hello\nworld" }"""],

        [FieldTypeKeys.RichText] =
        [
            """{ "type": "richText", "format": "markdown", "value": "# Hello" }""",
            """{ "type": "richText", "format": "html", "value": "<p>Hello</p>" }""",
        ],

        [FieldTypeKeys.Html] = ["""{ "type": "html", "value": "<p>Hello</p>" }"""],

        [FieldTypeKeys.Number] = ["""{ "type": "number", "value": 42.5 }"""],

        [FieldTypeKeys.Boolean] = ["""{ "type": "boolean", "value": true }"""],

        [FieldTypeKeys.Date] = ["""{ "type": "date", "value": "2026-08-13" }"""],

        [FieldTypeKeys.DateTime] = ["""{ "type": "dateTime", "value": "2026-08-13T09:30:00Z" }"""],

        [FieldTypeKeys.Choice] =
        [
            """{ "type": "choice", "value": "alpha" }""",
            """{ "type": "choice", "value": ["alpha", "beta"] }""",
        ],

        [FieldTypeKeys.Color] = ["""{ "type": "color", "value": "#1f6feb" }"""],

        [FieldTypeKeys.Json] = ["""{ "type": "json", "value": { "a": 1 } }"""],

        [FieldTypeKeys.Media] = ["""{ "type": "media", "mediaId": 812 }"""],

        [FieldTypeKeys.MediaList] = ["""{ "type": "mediaList", "items": [ { "mediaId": 812 } ] }"""],

        [FieldTypeKeys.Link] =
        [
            """{ "type": "link", "kind": "page", "pageId": 44 }""",
            """{ "type": "link", "kind": "external", "url": "https://example.com" }""",
        ],

        [FieldTypeKeys.PageReference] =
        [
            """{ "type": "pageReference", "value": 44 }""",
            """{ "type": "pageReference", "value": [44, 45] }""",
        ],

        [FieldTypeKeys.Reusable] = ["""{ "type": "reusable", "reusableContentId": 3 }"""],

        [FieldTypeKeys.Blocks] =
        [
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "properties": {} }
            ] }
            """,
        ],

        [FieldTypeKeys.Tags] = ["""{ "type": "tags", "value": ["release-notes", "v2"] }"""],
    };

    public static TheoryData<string> RegisteredKeys
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var fieldType in Registry().All)
            {
                data.Add(fieldType.Key);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RegisteredKeys))]
    public async Task EverySettingAFieldTypeReadsIsOneItDeclares(string key)
    {
        var fieldType = Registry().Find(key)!;

        PopulatedValues.Should().ContainKey(key,
            "a field type needs a representative populated value here, or nothing drives the " +
            "branches that read its configuration");

        // Backed by a configuration holding every declared setting, not by an empty one: most reads
        // are gated on another setting being present, and 'patternMessage' is read only once a
        // configured pattern has failed. Recording against an empty configuration sees none of
        // them and passes for a field type that declares nothing at all.
        var recording = new RecordingConfiguration(Sample(fieldType.ConfigurationSchema));

        foreach (var json in PopulatedValues[key])
        {
            await fieldType.ValidateAsync(
                FieldTypeTestHarness.Element(json),
                recording,
                ValidationMode.Publish,
                TestContext.Current.CancellationToken);

            await fieldType.SanitizeAsync(
                FieldTypeTestHarness.Element(json),
                recording,
                TestContext.Current.CancellationToken);
        }

        var declared = fieldType.ConfigurationSchema.Settings.Select(setting => setting.Name);

        // A setting read but never declared can never be stored, so the rule reading it is dead
        // code — and a closed schema is what makes that true rather than merely untidy.
        recording.Names.Should().BeSubsetOf(declared);
    }

    [Theory]
    [MemberData(nameof(RegisteredKeys))]
    public void EverySettingAFieldTypeDeclaresCanBeConfigured(string key)
    {
        var fieldType = Registry().Find(key)!;
        var schema = fieldType.ConfigurationSchema;

        if (schema.Settings.Count == 0) return;

        var configuration = Sample(schema);
        var result = Validator().Validate(key, configuration);

        // The other direction: a setting a field type declares but its own validator refuses is a
        // configuration a developer is invited to write and then told they may not have.
        result.HasErrors.Should().BeFalse(
            "the configuration {0} is built only from what {1} declares", configuration, key);
    }

    /// <summary>
    /// Builds a configuration exercising every setting a schema declares.
    /// </summary>
    /// <param name="schema">The schema to satisfy.</param>
    /// <returns>Configuration JSON holding one plausible value per declared setting.</returns>
    /// <remarks>
    /// Every value is derived from the declaration itself — the lowest permitted number, the first
    /// allowed value, a string in the declared syntax — so the test asserts the declaration is
    /// internally satisfiable rather than that it matches a table written alongside it. Both ends of
    /// a range land on the same value, which is why an inverted range would still pass here and is
    /// covered separately.
    /// </remarks>
    private static string Sample(FieldConfigurationSchema schema)
    {
        var members = schema.Settings.Select(setting =>
            $"\"{setting.Name}\": {SampleValue(setting)}");

        return $"{{ {string.Join(", ", members)} }}";
    }

    private static string SampleValue(FieldConfigurationSetting setting) => setting.Kind switch
    {
        FieldSettingKind.Boolean => "true",

        FieldSettingKind.Integer or FieldSettingKind.Number =>
            (setting.Minimum ?? 1) + (setting.MinimumExclusive ? 1 : 0) is var number &&
            setting.Maximum is { } maximum && number > maximum
                ? maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : number.ToString(System.Globalization.CultureInfo.InvariantCulture),

        FieldSettingKind.Text => JsonSerializer.Serialize(SampleText(setting)),

        FieldSettingKind.TextList => $"[{JsonSerializer.Serialize(SampleText(setting))}]",

        _ => "null",
    };

    private static string SampleText(FieldConfigurationSetting setting)
    {
        if (setting.AllowedValues.Count > 0) return setting.AllowedValues[0];

        return setting.Format switch
        {
            // Matches nothing the sample values hold, so a field type validating against a pattern
            // reaches the branch that reads 'patternMessage'.
            FieldSettingFormat.RegularExpression => "^$",
            FieldSettingFormat.Date => "2026-08-13",
            FieldSettingFormat.DateTime => "2026-08-13T09:30:00Z",
            FieldSettingFormat.HexColor => "#1f6feb",
            _ => "sample",
        };
    }

    private static IFieldTypeRegistry Registry() => Provider().GetRequiredService<IFieldTypeRegistry>();

    private static IFieldConfigurationValidator Validator() =>
        Provider().GetRequiredService<IFieldConfigurationValidator>();

    private static ServiceProvider Provider() =>
        new ServiceCollection()
            .AddSingleton<IContentSanitizer, RecordingSanitizer>()
            .AddCmsFieldTypes()
            .BuildServiceProvider();

    /// <summary>
    /// A configuration that records which settings were asked for.
    /// </summary>
    /// <remarks>
    /// Every typed accessor funnels through <see cref="FieldConfiguration.TryGetValue"/>, so
    /// overriding that one member sees every read. It answers from a real configuration rather than
    /// an empty one, so that reads gated on another setting being present are reached.
    /// </remarks>
    private sealed class RecordingConfiguration : FieldConfiguration
    {
        public RecordingConfiguration(string configurationJson)
            : base(FieldTypeTestHarness.Element(configurationJson), true)
        {
        }

        /// <summary>Every setting name looked up, in no particular order.</summary>
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public override bool TryGetValue(string name, out JsonElement value)
        {
            Names.Add(name);

            return base.TryGetValue(name, out value);
        }
    }
}
