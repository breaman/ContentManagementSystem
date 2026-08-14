using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A pointer to one or several other pages in the content tree — "related articles", "featured
/// page" (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "pageReference", "value": 44 }</c>, or with an array of ids under the same
/// member when the property is configured for multiple selection. One member either way, following
/// <see cref="ChoiceFieldType"/>: a renderer that has to look in two places for the same thing
/// eventually looks in only one.
/// <para>
/// Like <see cref="LinkFieldType"/>, this stores identity and never a URL (decision D6). Unlike it,
/// it carries no link text or target — it names a page and lets the renderer decide what to show,
/// which is what makes a card list pick up a retitled page without being re-authored.
/// </para>
/// <para>
/// Configuration keys: <c>multiple</c>, <c>min</c> / <c>max</c> counts, and the P3 addition
/// <c>allowedTemplates</c>.
/// </para>
/// <para>
/// <strong>Completed in P3</strong>: the content-tree picker, the <c>allowedTemplates</c>
/// restriction, and the check that the target exists and is not in the recycle bin. Reference
/// extraction ships now — see <see cref="MediaFieldType"/> for why.
/// </para>
/// </remarks>
public sealed class PageReferenceFieldType : FieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.PageReference;

    /// <inheritdoc />
    public override string DisplayName => "Page reference";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.ReferenceBearing;
    /// <inheritdoc />
    public override FieldConfigurationSchema ConfigurationSchema { get; } = new(
        [
            FieldConfigurationSetting.Boolean(
                "multiple",
                "Whether several pages may be referenced, storing an array of ids rather than one."),
            FieldConfigurationSetting.Integer(
                "min",
                "Fewest pages that must be referenced. Only applies when multiple is set.",
                minimum: 0),
            FieldConfigurationSetting.Integer(
                "max",
                "Most pages that may be referenced. Only applies when multiple is set.",
                minimum: 0),
            FieldConfigurationSetting.TextList(
                "allowedTemplates",
                "Keys of the templates a referenced page may use. An empty list allows any page.",
                notEnforcedUntil: "P3"),
        ],
        [new FieldSettingRange("min", "max")]);


    /// <inheritdoc />
    protected override bool IsEmpty(JsonElement value) =>
        base.IsEmpty(value) ||
        (value.ValueKind is JsonValueKind.Array && value.GetArrayLength() == 0);

    /// <inheritdoc />
    /// <remarks>
    /// A configured minimum count says the same thing as <c>required</c> and has to reach the empty
    /// list too, or it is the one count rule an unfilled property slips past.
    /// </remarks>
    protected override ValidationResult ValidateEmpty(FieldConfiguration configuration, ValidationMode mode)
    {
        if (mode is ValidationMode.Publish &&
            configuration.GetBoolean("multiple") &&
            configuration.GetInt32("min") is { } min &&
            min > 0)
        {
            return ValidationResult.Error(
                FieldValidationCodes.MinItems,
                $"Choose at least {min} pages.",
                ValueMember);
        }

        return base.ValidateEmpty(configuration, mode);
    }

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode) =>
        configuration.GetBoolean("multiple")
            ? ValidateMany(value, configuration)
            : ValidateOne(value);

    /// <inheritdoc />
    public override IEnumerable<ContentReference> ExtractReferences(JsonElement value)
    {
        var stored = GetValue(value);

        if (stored.ValueKind is JsonValueKind.Array)
        {
            var index = 0;

            foreach (var item in stored.EnumerateArray())
            {
                if (StoredId.TryRead(item, out var id))
                {
                    yield return new ContentReference(
                        ContentReferenceTargetType.Page,
                        id,
                        RelativePaths.Index(ValueMember, index));
                }

                index++;
            }
        }
        else if (StoredId.TryRead(stored, out var pageId))
        {
            yield return new ContentReference(ContentReferenceTargetType.Page, pageId);
        }
    }

    private static ValidationResult ValidateOne(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Array)
        {
            return ValidationResult.Error(
                FieldValidationCodes.Shape,
                "This property holds a single page.",
                ValueMember);
        }

        return StoredId.TryRead(value, out _)
            ? ValidationResult.Success
            : ValidationResult.Error(
                FieldValidationCodes.ReferenceId,
                "This does not identify a page.",
                ValueMember);
    }

    private static ValidationResult ValidateMany(JsonElement value, FieldConfiguration configuration)
    {
        if (value.ValueKind is not JsonValueKind.Array)
        {
            return ValidationResult.Error(
                FieldValidationCodes.Shape,
                "Expected a list of pages.",
                ValueMember);
        }

        List<ValidationDiagnostic>? diagnostics = null;
        var seen = new HashSet<int>();
        var count = 0;

        foreach (var item in value.EnumerateArray())
        {
            var path = RelativePaths.Index(ValueMember, count);

            if (!StoredId.TryRead(item, out var id))
            {
                Diagnostics.AddError(
                    ref diagnostics,
                    FieldValidationCodes.ReferenceId,
                    "This does not identify a page.",
                    path);
            }
            else if (!seen.Add(id))
            {
                // The same page twice in one list is a picker double-click, not an intention: it
                // renders a duplicate card and doubles the page's weight in any "related" ordering.
                Diagnostics.AddError(
                    ref diagnostics,
                    FieldValidationCodes.Duplicate,
                    "This page is already in the list.",
                    path);
            }

            count++;
        }

        if (configuration.GetInt32("min") is { } min && count < min)
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.MinItems,
                $"Choose at least {min} pages; there are {count}.",
                ValueMember);
        }

        if (configuration.GetInt32("max") is { } max && count > max)
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.MaxItems,
                $"Choose at most {max} pages; there are {count}.",
                ValueMember);
        }

        return Result(diagnostics);
    }

    /// <inheritdoc />
    public override JsonNode? RemapReferences(JsonElement value, ReferenceRemapper remap)
    {
        ArgumentNullException.ThrowIfNull(remap);

        if (ReferenceRemapping.Clone(value) is not { } copy) return null;

        return ReferenceRemapping.RemapIdOrArray(
            copy, ValueMember, ContentReferenceTargetType.Page, remap)
            ? copy
            : null;
    }
}
