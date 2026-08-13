using System.Text.Json;

using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Configuration;

/// <inheritdoc />
public sealed class FieldConfigurationValidator : IFieldConfigurationValidator
{
    private readonly IFieldTypeRegistry _registry;

    /// <summary>
    /// Builds the validator over the registered field types.
    /// </summary>
    /// <param name="registry">The field type registry.</param>
    public FieldConfigurationValidator(IFieldTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unlike content validation, an unknown field type key is an <em>error</em> here rather than a
    /// logged warning. Delivery has to be forgiving because the payload is already stored and a
    /// visitor is waiting; a zone save is the moment before anything is stored, and binding a zone
    /// to a field type no deployment has is a structure the editor could never render.
    /// </remarks>
    public ValidationResult Validate(string fieldTypeKey, string? configurationJson)
    {
        var fieldType = _registry.Find(fieldTypeKey);

        if (fieldType is null)
        {
            return ValidationResult.Error(
                FieldConfigurationCodes.UnknownFieldType,
                $"'{fieldTypeKey}' is not a registered field type.");
        }

        // A zone need not configure anything, and the overwhelming majority do not.
        if (string.IsNullOrWhiteSpace(configurationJson)) return ValidationResult.Success;

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(configurationJson);
        }
        catch (JsonException exception)
        {
            return ValidationResult.Error(
                FieldConfigurationCodes.Malformed,
                $"Configuration is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object)
            {
                return ValidationResult.Error(
                    FieldConfigurationCodes.Shape,
                    "Configuration must be a JSON object of settings.");
            }

            return Validate(root, fieldType.ConfigurationSchema);
        }
    }

    private static ValidationResult Validate(JsonElement root, FieldConfigurationSchema schema)
    {
        List<ValidationDiagnostic>? diagnostics = null;

        foreach (var member in root.EnumerateObject())
        {
            // An explicit null reads the same as an absent setting everywhere else
            // (FieldConfiguration.TryGetValue), so it is not worth refusing here.
            if (member.Value.ValueKind is JsonValueKind.Null) continue;

            if (string.Equals(member.Name, FieldConfigurationSchema.RequiredSettingName, StringComparison.Ordinal))
            {
                Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                    FieldConfigurationCodes.RequiredReserved,
                    "Whether a value is required is set on the zone itself, not in its " +
                    "configuration. Two copies of that flag would be free to disagree.",
                    ValidationSeverity.Error,
                    member.Name));

                continue;
            }

            if (schema.Find(member.Name) is not { } setting)
            {
                Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                    FieldConfigurationCodes.UnknownSetting,
                    UnknownSettingMessage(member.Name, schema),
                    ValidationSeverity.Error,
                    member.Name));

                continue;
            }

            ValidateSetting(member.Value, setting, ref diagnostics);
        }

        ValidateRanges(root, schema, ref diagnostics);

        return Diagnostics.Result(diagnostics);
    }

    /// <summary>
    /// Explains an unrecognised setting, naming a near miss when there is an obvious one.
    /// </summary>
    /// <param name="name">The setting as written.</param>
    /// <param name="schema">The schema it was checked against.</param>
    /// <remarks>
    /// Only a case-insensitive match counts as obvious. Guessing further — an edit-distance
    /// suggestion — would point a developer at a setting they did not mean often enough to be worse
    /// than saying nothing.
    /// </remarks>
    private static string UnknownSettingMessage(string name, FieldConfigurationSchema schema)
    {
        foreach (var setting in schema.Settings)
        {
            if (string.Equals(setting.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"'{name}' is not a setting of this field type. Settings are case-sensitive; " +
                    $"did you mean '{setting.Name}'?";
            }
        }

        return schema.Settings.Count == 0
            ? $"This field type takes no configuration, so '{name}' would have no effect."
            : $"'{name}' is not a setting of this field type. It accepts: " +
                $"{string.Join(", ", schema.Settings.Select(declared => declared.Name))}.";
    }

    private static void ValidateSetting(
        JsonElement value,
        FieldConfigurationSetting setting,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (!HasExpectedKind(value, setting.Kind))
        {
            Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                FieldConfigurationCodes.SettingType,
                $"'{setting.Name}' must be {Describe(setting.Kind)}.",
                ValidationSeverity.Error,
                setting.Name));

            return;
        }

        switch (setting.Kind)
        {
            case FieldSettingKind.Integer or FieldSettingKind.Number:
                ValidateBounds(value, setting, ref diagnostics);
                break;

            case FieldSettingKind.Text:
                ValidateText(value, setting, setting.Name, ref diagnostics);
                break;

            case FieldSettingKind.TextList:
                var index = 0;

                foreach (var item in value.EnumerateArray())
                {
                    ValidateText(item, setting, RelativePaths.Index(setting.Name, index), ref diagnostics);
                    index++;
                }

                break;

            case FieldSettingKind.Boolean:
            default:
                break;
        }

        if (setting.NotEnforcedUntil is { Length: > 0 } phase)
        {
            // Stored, not refused. The configuration is correct; the code that reads it has not
            // shipped, and a developer setting up a content model should hear that once rather than
            // be told to come back later.
            Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                FieldConfigurationCodes.NotEnforced,
                $"'{setting.Name}' is stored but not yet enforced; it starts applying in {phase}.",
                ValidationSeverity.Warning,
                setting.Name));
        }
    }

    private static void ValidateBounds(
        JsonElement value,
        FieldConfigurationSetting setting,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (!value.TryGetDecimal(out var number)) return;

        if (setting.Minimum is { } minimum &&
            (setting.MinimumExclusive ? number <= minimum : number < minimum))
        {
            Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                FieldConfigurationCodes.SettingRange,
                setting.MinimumExclusive
                    ? $"'{setting.Name}' must be greater than {minimum}."
                    : $"'{setting.Name}' must be {minimum} or more.",
                ValidationSeverity.Error,
                setting.Name));
        }

        if (setting.Maximum is { } maximum && number > maximum)
        {
            Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                FieldConfigurationCodes.SettingRange,
                $"'{setting.Name}' must be {maximum} or less.",
                ValidationSeverity.Error,
                setting.Name));
        }
    }

    private static void ValidateText(
        JsonElement value,
        FieldConfigurationSetting setting,
        string path,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                FieldConfigurationCodes.SettingType,
                $"Every item of '{setting.Name}' must be text.",
                ValidationSeverity.Error,
                path));

            return;
        }

        var text = value.GetString()!;

        if (setting.AllowedValues.Count > 0 &&
            !setting.AllowedValues.Contains(text, StringComparer.Ordinal))
        {
            Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                FieldConfigurationCodes.SettingValue,
                $"'{text}' is not one of: {string.Join(", ", setting.AllowedValues)}.",
                ValidationSeverity.Error,
                path));
        }

        if (FormatProblem(text, setting.Format) is { } problem)
        {
            Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                FieldConfigurationCodes.SettingFormat,
                problem,
                ValidationSeverity.Error,
                path));
        }
    }

    /// <summary>
    /// Checks a string against the syntax its field type will later have to parse.
    /// </summary>
    /// <param name="text">The configured value.</param>
    /// <param name="format">The syntax required.</param>
    /// <returns>What is wrong with it, or null when nothing is.</returns>
    private static string? FormatProblem(string text, FieldSettingFormat format) => format switch
    {
        FieldSettingFormat.RegularExpression when !FieldPatterns.IsUsable(text) =>
            $"'{text}' is not a usable regular expression.",

        FieldSettingFormat.Date when !ValueFormats.TryParseDate(text, out _) =>
            $"'{text}' is not a date written as YYYY-MM-DD.",

        FieldSettingFormat.DateTime when !ValueFormats.TryParseInstant(text, out _) ||
            !ValueFormats.HasExplicitOffset(text) =>
            $"'{text}' is not a date and time written as YYYY-MM-DDThh:mm:ssZ, including the offset.",

        FieldSettingFormat.HexColor when !ValueFormats.IsHexColor(text) =>
            $"'{text}' is not a colour written as #RRGGBB.",

        _ => null,
    };

    /// <summary>
    /// Checks the pairs of settings that bound each other.
    /// </summary>
    /// <param name="root">The configuration object.</param>
    /// <param name="schema">The schema being checked against.</param>
    /// <param name="diagnostics">Collected diagnostics, allocated on first use.</param>
    /// <remarks>
    /// The one rule JSON Schema cannot express, and the one worth most: <c>{ "min": 5, "max": 2 }</c>
    /// is not an odd configuration, it is one no value can satisfy, and without this check the
    /// contradiction surfaces as an editor who cannot publish and cannot see why.
    /// </remarks>
    private static void ValidateRanges(
        JsonElement root,
        FieldConfigurationSchema schema,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        foreach (var range in schema.Ranges)
        {
            if (!TryReadComparable(root, schema, range.LowerName, out var lower) ||
                !TryReadComparable(root, schema, range.UpperName, out var upper) ||
                lower <= upper)
            {
                continue;
            }

            Diagnostics.Add(ref diagnostics, new ValidationDiagnostic(
                FieldConfigurationCodes.RangeInverted,
                $"'{range.LowerName}' is above '{range.UpperName}', so no value can satisfy both.",
                ValidationSeverity.Error,
                range.LowerName));
        }
    }

    /// <summary>
    /// Reads a bound as a number that can be compared with the other end of its range.
    /// </summary>
    /// <param name="root">The configuration object.</param>
    /// <param name="schema">The schema being checked against.</param>
    /// <param name="name">The setting to read.</param>
    /// <param name="comparable">The value on a scale shared with its pair.</param>
    /// <returns><see langword="false"/> when the setting is absent or already reported as invalid.</returns>
    private static bool TryReadComparable(
        JsonElement root,
        FieldConfigurationSchema schema,
        string name,
        out decimal comparable)
    {
        comparable = 0;

        if (!root.TryGetProperty(name, out var value) || schema.Find(name) is not { } setting)
        {
            return false;
        }

        switch (setting.Kind)
        {
            case FieldSettingKind.Integer or FieldSettingKind.Number:
                return value.ValueKind is JsonValueKind.Number && value.TryGetDecimal(out comparable);

            case FieldSettingKind.Text when setting.Format is FieldSettingFormat.Date:
                if (value.ValueKind is not JsonValueKind.String ||
                    !ValueFormats.TryParseDate(value.GetString(), out var date))
                {
                    return false;
                }

                comparable = date.DayNumber;

                return true;

            case FieldSettingKind.Text when setting.Format is FieldSettingFormat.DateTime:
                if (value.ValueKind is not JsonValueKind.String ||
                    !ValueFormats.TryParseInstant(value.GetString(), out var instant))
                {
                    return false;
                }

                comparable = instant.UtcTicks;

                return true;

            default:
                return false;
        }
    }

    private static bool HasExpectedKind(JsonElement value, FieldSettingKind kind) => kind switch
    {
        FieldSettingKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,

        // A fractional maxLength is not a length. FieldConfiguration.GetInt32 would return null for
        // it and the setting would be silently ignored.
        FieldSettingKind.Integer => value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out _),

        FieldSettingKind.Number => value.ValueKind is JsonValueKind.Number && value.TryGetDecimal(out _),

        FieldSettingKind.Text => value.ValueKind is JsonValueKind.String,

        FieldSettingKind.TextList => value.ValueKind is JsonValueKind.Array,

        _ => false,
    };

    private static string Describe(FieldSettingKind kind) => kind switch
    {
        FieldSettingKind.Boolean => "true or false",
        FieldSettingKind.Integer => "a whole number",
        FieldSettingKind.Number => "a number",
        FieldSettingKind.Text => "text",
        FieldSettingKind.TextList => "a list of text values",
        _ => "a supported value",
    };
}
