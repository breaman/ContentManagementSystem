using System.Buffers;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Configuration;

/// <summary>
/// Renders a <see cref="FieldConfigurationSchema"/> as the JSON Schema document spec section 7.2
/// calls for.
/// </summary>
/// <remarks>
/// The schema is declared in C# and the JSON Schema is generated from it, rather than the other way
/// round (ADR 0015). What the document is for is everything on the far side of the wire: the
/// read-only <c>/api/cms/v1/field-types</c> endpoint (<c>P1-24</c>) serves it, the zone
/// configuration form in <c>P1-29</c> builds its controls from it, and an editor or a deployment
/// script can check a configuration before sending it.
/// <para>
/// Authoritative validation stays on the server in <see cref="FieldConfigurationValidator"/>. Two of
/// the rules here cannot be said in JSON Schema at all — that a <c>pattern</c> compiles under .NET,
/// and that a lower bound is below its upper bound — so they are carried as <c>x-cms</c>
/// annotations, and a client that only ran the document would accept configurations the server
/// refuses.
/// </para>
/// </remarks>
public static class FieldConfigurationSchemaWriter
{
    /// <summary>The dialect the generated documents declare.</summary>
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>Matches an <c>#RRGGBB</c> colour, for the format JSON Schema has no keyword for.</summary>
    private const string HexColorPattern = "^#[0-9a-fA-F]{6}$";

    /// <summary>
    /// Renders the configuration schema of one field type.
    /// </summary>
    /// <param name="fieldType">The field type to describe.</param>
    /// <param name="indented">Whether to write human-readable JSON.</param>
    /// <returns>A JSON Schema document describing that field type's <c>ConfigurationJson</c>.</returns>
    /// <example>
    /// <code>
    /// var document = FieldConfigurationSchemaWriter.Write(registry.Find("blocks")!);
    /// </code>
    /// </example>
    public static string Write(IFieldType fieldType, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(fieldType);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            Write(writer, fieldType);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Writes the configuration schema of one field type to an open writer.
    /// </summary>
    /// <param name="writer">The writer, positioned where a value may be written.</param>
    /// <param name="fieldType">The field type to describe.</param>
    /// <remarks>
    /// Exposed separately so the <c>/field-types</c> endpoint can embed the document in a larger
    /// response without serialising it to a string and parsing it back.
    /// </remarks>
    public static void Write(Utf8JsonWriter writer, IFieldType fieldType)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(fieldType);

        var schema = fieldType.ConfigurationSchema;

        writer.WriteStartObject();
        writer.WriteString("$schema", Dialect);
        writer.WriteString("$id", $"urn:cms:field-configuration:{fieldType.Key}");
        writer.WriteString("title", $"{fieldType.DisplayName} configuration");
        writer.WriteString("type", "object");

        // The point of the whole exercise: a setting the field type does not declare is refused,
        // so a mistyped 'maxlength' is a save error rather than a line that quietly does nothing.
        writer.WriteBoolean("additionalProperties", false);

        writer.WriteStartObject("properties");

        foreach (var setting in schema.Settings)
        {
            WriteSetting(writer, setting);
        }

        writer.WriteEndObject();

        if (schema.Ranges.Count > 0)
        {
            WriteRanges(writer, schema);
        }

        writer.WriteEndObject();
    }

    private static void WriteSetting(Utf8JsonWriter writer, FieldConfigurationSetting setting)
    {
        writer.WriteStartObject(setting.Name);
        writer.WriteString("description", Describe(setting));

        switch (setting.Kind)
        {
            case FieldSettingKind.Boolean:
                writer.WriteString("type", "boolean");
                break;

            case FieldSettingKind.Integer:
                writer.WriteString("type", "integer");
                WriteBounds(writer, setting);
                break;

            case FieldSettingKind.Number:
                writer.WriteString("type", "number");
                WriteBounds(writer, setting);
                break;

            case FieldSettingKind.Text:
                writer.WriteString("type", "string");
                WriteTextConstraints(writer, setting);
                break;

            case FieldSettingKind.TextList:
                writer.WriteString("type", "array");
                writer.WriteStartObject("items");
                writer.WriteString("type", "string");
                WriteTextConstraints(writer, setting);
                writer.WriteEndObject();
                break;

            default:
                break;
        }

        if (setting.NotEnforcedUntil is { Length: > 0 } phase)
        {
            writer.WriteString("x-cmsNotEnforcedUntil", phase);
        }

        writer.WriteEndObject();
    }

    private static void WriteBounds(Utf8JsonWriter writer, FieldConfigurationSetting setting)
    {
        if (setting.Minimum is { } minimum)
        {
            writer.WriteNumber(setting.MinimumExclusive ? "exclusiveMinimum" : "minimum", minimum);
        }

        if (setting.Maximum is { } maximum)
        {
            writer.WriteNumber("maximum", maximum);
        }
    }

    private static void WriteTextConstraints(Utf8JsonWriter writer, FieldConfigurationSetting setting)
    {
        if (setting.AllowedValues.Count > 0)
        {
            writer.WriteStartArray("enum");

            foreach (var allowed in setting.AllowedValues)
            {
                writer.WriteStringValue(allowed);
            }

            writer.WriteEndArray();
        }

        switch (setting.Format)
        {
            case FieldSettingFormat.RegularExpression:
                // "regex" is an annotation here, not a guarantee: JSON Schema's regex format is
                // ECMA-262, and the server compiles the value with .NET's engine.
                writer.WriteString("format", "regex");
                break;

            case FieldSettingFormat.Date:
                writer.WriteString("format", "date");
                break;

            case FieldSettingFormat.DateTime:
                writer.WriteString("format", "date-time");
                break;

            case FieldSettingFormat.HexColor:
                writer.WriteString("pattern", HexColorPattern);
                break;

            case FieldSettingFormat.None:
            default:
                break;
        }
    }

    /// <summary>
    /// Writes the pairs of settings that bound each other as an annotation.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="schema">The schema being rendered.</param>
    /// <remarks>
    /// JSON Schema has no way to say "this property must not exceed that one", so this is an
    /// <c>x-cms</c> extension keyword. A validator that does not know it ignores it, which is why
    /// the server rule in <see cref="FieldConfigurationValidator"/> is the authoritative one.
    /// </remarks>
    private static void WriteRanges(Utf8JsonWriter writer, FieldConfigurationSchema schema)
    {
        writer.WriteStartArray("x-cmsOrderedRanges");

        foreach (var range in schema.Ranges)
        {
            writer.WriteStartObject();
            writer.WriteString("lower", range.LowerName);
            writer.WriteString("upper", range.UpperName);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string Describe(FieldConfigurationSetting setting) =>
        setting.NotEnforcedUntil is { Length: > 0 } phase
            ? $"{setting.Description} Not enforced until {phase}."
            : setting.Description;
}
