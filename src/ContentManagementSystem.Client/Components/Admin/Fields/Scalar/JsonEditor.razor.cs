using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Scalar;

/// <summary>
/// The <c>json</c> editor — the escape hatch, edited as text (spec section 7.1).
/// </summary>
/// <remarks>
/// <strong>The draft is text, and the stored value is only rewritten while the text parses.</strong>
/// That is the whole design. JSON is unparseable for most of the time it is being typed — the moment
/// after an opening brace, every moment between a key and its value — and an editor that wrote
/// through on every keystroke would either store rubbish or throw the keystroke away. So the
/// component holds what was typed, says whether it parses, and writes only when it does.
/// <para>
/// The consequence is worth being explicit about: text that never becomes valid JSON is never saved.
/// The state line says so continuously rather than at save time, which is the only moment early
/// enough to be useful.
/// </para>
/// <para>
/// This field type is <c>DeveloperOnly</c>, so the person in front of it is the person who put the
/// property on the template. The note about search and references is for them: it is a limit of the
/// field type rather than a gap, and it is cheaper to read here than to discover from a page that
/// stopped invalidating.
/// </para>
/// </remarks>
public partial class JsonEditor : FieldEditorBase
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>What is in the box, which is not always something that can be stored.</summary>
    private string Draft { get; set; } = string.Empty;

    /// <summary>Why the draft cannot be stored, or null when it can.</summary>
    private string? ParseError { get; set; }

    private string StateId => $"{Field.ControlId}-state";

    private string ScopeId => $"{Field.ControlId}-scope";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, StateId, ScopeId }.Where(id => !string.IsNullOrEmpty(id)));

    private string InvalidClass =>
        ParseError is not null || Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// Re-reads only when the value came from somewhere else, so the box is not reset under an
    /// author on every keystroke it just caused.
    /// </remarks>
    protected override void OnParametersSet()
    {
        if (!IsExternalChange) return;

        Draft = ReadDraft(Value);
        ParseError = null;
    }

    /// <summary>Reads the stored inner value as indented text.</summary>
    /// <remarks>
    /// The <c>value</c> member alone, not the envelope: the discriminator is this component's to
    /// maintain, and showing it in the box would invite an author to edit it.
    /// </remarks>
    private static string ReadDraft(string? json)
    {
        if (StoredValue.Parse(json) is not { } stored ||
            stored[StoredValue.ValueMember] is not { } value)
        {
            return string.Empty;
        }

        return value.ToJsonString(Indented);
    }

    /// <summary>Parses what was typed and stores it when it is storable.</summary>
    private Task OnInputAsync(ChangeEventArgs args)
    {
        Draft = args.Value?.ToString() ?? string.Empty;

        if (Draft.Trim().Length == 0)
        {
            ParseError = null;

            return WriteAsync(string.Empty);
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(Draft);
        }
        catch (JsonException exception)
        {
            // The message rather than the exception type: System.Text.Json says where it stopped and
            // what it wanted, which is the useful half, and there is nothing to be done about it in
            // code.
            ParseError = exception.Message;

            return Task.CompletedTask;
        }

        ParseError = null;

        return WriteAsync(StoredValue.Write(Value, FieldTypeKey, parsed));
    }

    /// <summary>Rewrites the box from the stored value, indented.</summary>
    private void Reformat() => Draft = ReadDraft(Value);
}
