using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Scalar;

/// <summary>
/// The <c>choice</c> editor — one value or several, from a configured list (spec section 7.1).
/// </summary>
/// <remarks>
/// Three controls behind one field type, chosen by configuration: a select when there are options
/// and one may be picked, a checkbox group when several may, and a plain text box when the property
/// configures no options at all — which the field type reads as "accepts any value", and which a
/// select could only render as an empty menu.
/// <para>
/// <strong>What is stored is the option key, never the label.</strong> Here they are the same
/// string, because the configuration is a flat list; that is a property of the current configuration
/// shape rather than a licence to store what is displayed. If <c>options</c> ever grows a
/// key-and-label form, this control changes and the payloads it has already written do not.
/// </para>
/// </remarks>
public partial class ChoiceEditor : FieldEditorBase
{
    /// <summary>The values the slot offers, empty when it accepts anything.</summary>
    private IReadOnlyList<string> Options => ConfiguredTextList(FieldSettingNames.Options);

    /// <summary>Whether several values may be chosen.</summary>
    private bool IsMultiple => ConfiguredBoolean(FieldSettingNames.Multiple);

    /// <summary>The chosen values, however many the property allows.</summary>
    private IReadOnlyList<string> Chosen => StoredValue.ReadTextList(Value);

    /// <summary>The single chosen value, or empty.</summary>
    private string Single => Chosen.Count > 0 ? Chosen[0] : string.Empty;

    /// <summary>Fewest values that must be chosen.</summary>
    private int? Min => ConfiguredInt32(FieldSettingNames.Min);

    /// <summary>Most values that may be chosen.</summary>
    private int? Max => ConfiguredInt32(FieldSettingNames.Max);

    private string CountId => $"{Field.ControlId}-count";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, CountRule is { Length: > 0 } ? CountId : null }
            .Where(id => !string.IsNullOrEmpty(id)));

    private string InvalidClass => Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    /// <summary>How many may be chosen, said in words as well as enforced at publish.</summary>
    private string? CountRule => (Min, Max) switch
    {
        (null, null) => null,
        ({ } min, { } max) when min == max => $"Choose exactly {Plural(min)}.",
        ({ } min, { } max) => $"Choose between {min} and {Plural(max)}.",
        ({ } min, null) => $"Choose at least {Plural(min)}.",
        (null, { } max) => $"Choose at most {Plural(max)}.",
    };

    /// <summary>
    /// A stable, unique element id for an option's checkbox.
    /// </summary>
    /// <remarks>
    /// The option's position is part of the id, not decoration. Option values are author-configured
    /// text and can hold spaces, dots, and anything else a <c>for</c> attribute would choke on, so
    /// they are slugged — and two options differing only in punctuation slug to the same string,
    /// which would tie two labels to one checkbox. The index is what stops that.
    /// </remarks>
    private string OptionId(int index, string option) =>
        $"{Field.ControlId}-{index}-{Slug(option)}";

    private static string Plural(int count) => count == 1 ? "1 option" : $"{count} options";

    /// <summary>Collapses an option value to characters an element id can hold.</summary>
    private static string Slug(string option)
    {
        var characters = option.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');

        return new string([.. characters]);
    }

    /// <summary>Stores one chosen value, removing the slot when nothing is chosen.</summary>
    private Task OnTypedAsync(ChangeEventArgs args)
    {
        var chosen = args.Value?.ToString();

        return string.IsNullOrEmpty(chosen)
            ? WriteAsync(string.Empty)
            : WriteAsync(StoredValue.Write(Value, FieldTypeKey, JsonValue.Create(chosen)));
    }

    /// <summary>
    /// Adds or removes one option, keeping the configured option order.
    /// </summary>
    /// <remarks>
    /// Order follows the option list rather than the order they were clicked in, so two pages that
    /// chose the same three options produce the same payload — which keeps a version diff about what
    /// changed rather than about what was clicked first.
    /// </remarks>
    private Task OnToggledAsync(string option, ChangeEventArgs args)
    {
        var isOn = (bool?)args.Value ?? false;
        var chosen = new HashSet<string>(Chosen, StringComparer.Ordinal);

        if (isOn)
        {
            chosen.Add(option);
        }
        else
        {
            chosen.Remove(option);
        }

        var ordered = Options.Where(chosen.Contains).Concat(chosen.Except(Options)).ToList();

        return WriteAsync(ordered.Count == 0
            ? string.Empty
            : StoredValue.Write(Value, FieldTypeKey, StoredValue.TextList(ordered)));
    }
}
