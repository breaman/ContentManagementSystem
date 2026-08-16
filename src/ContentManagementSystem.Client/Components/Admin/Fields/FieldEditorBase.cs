using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields;

/// <summary>
/// The three parameters every field editor takes, so that one host can render any of them
/// (ADR-0014, tasks P6-06 to P6-15).
/// </summary>
/// <remarks>
/// A base class rather than a convention, because the convention has to hold for editors this
/// repository will never see: <see cref="FieldEditorHost"/> passes these three by name through
/// <c>DynamicComponent</c>, and a third-party editor that spelled one of them differently would
/// throw at render time with a message about an unrecognised parameter rather than at registration
/// with one about the contract.
/// <para>
/// <see cref="Value"/> is the stored value as JSON text — the whole
/// <c>{ "type": …, "value": … }</c> envelope, not the text inside it. Handing each editor the
/// envelope is what keeps the shape of a field type's storage in the one component that understands
/// it, instead of in a switch statement in the form around it; <see cref="StoredValue"/> is the
/// shared reader and writer for it.
/// </para>
/// </remarks>
public abstract class FieldEditorBase : ComponentBase
{
    /// <summary>The slot, and the ids the frame wants the control to carry.</summary>
    [Parameter]
    [EditorRequired]
    public FieldEditorContext Field { get; set; } = default!;

    /// <summary>The stored value as JSON text, empty when nothing has been authored.</summary>
    [Parameter]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Raised with the rewritten JSON whenever the value changes, or with the empty string when the
    /// author has cleared it.
    /// </summary>
    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>The field type key this editor is drawing.</summary>
    protected string FieldTypeKey => Field.Slot.FieldTypeKey;

    /// <summary>The slot's field-type configuration, as the revision captured it.</summary>
    protected JsonElement? Configuration => Field.Slot.Configuration;

    /// <summary>
    /// The last value this editor wrote, so that its own writes do not re-read as external ones.
    /// </summary>
    /// <remarks>
    /// Every editor that keeps parsed state needs this guard. Without it each keystroke the control
    /// writes is parsed straight back in by <c>OnParametersSet</c> and resets the box being typed in
    /// — the same echo the JS editors suppress across the interop boundary, one layer up.
    /// </remarks>
    protected string? LastWritten { get; private set; }

    /// <summary>Whether the value arriving in parameters came from somewhere other than this editor.</summary>
    protected bool IsExternalChange => !string.Equals(Value, LastWritten, StringComparison.Ordinal);

    /// <summary>An integer configuration setting, or null when the slot does not configure it.</summary>
    /// <param name="setting">The setting's name.</param>
    /// <returns>The configured value.</returns>
    protected int? ConfiguredInt32(string setting) => ReadInt32(Configuration, setting);

    /// <summary>A decimal configuration setting, or null when the slot does not configure it.</summary>
    /// <param name="setting">The setting's name.</param>
    /// <returns>The configured value.</returns>
    protected decimal? ConfiguredDecimal(string setting) =>
        Configuration is { } configuration &&
        configuration.ValueKind is JsonValueKind.Object &&
        configuration.TryGetProperty(setting, out var value) &&
        value.ValueKind is JsonValueKind.Number &&
        value.TryGetDecimal(out var number)
            ? number
            : null;

    /// <summary>A boolean configuration setting, false when the slot does not configure it.</summary>
    /// <param name="setting">The setting's name.</param>
    /// <returns>Whether the setting is on.</returns>
    protected bool ConfiguredBoolean(string setting) =>
        Configuration is { } configuration &&
        configuration.ValueKind is JsonValueKind.Object &&
        configuration.TryGetProperty(setting, out var value) &&
        value.ValueKind is JsonValueKind.True;

    /// <summary>A text configuration setting, or null when the slot does not configure it.</summary>
    /// <param name="setting">The setting's name.</param>
    /// <returns>The configured value.</returns>
    protected string? ConfiguredText(string setting) =>
        Configuration is { } configuration &&
        configuration.ValueKind is JsonValueKind.Object &&
        configuration.TryGetProperty(setting, out var value) &&
        value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A text-list configuration setting, empty when the slot does not configure it.</summary>
    /// <param name="setting">The setting's name.</param>
    /// <returns>The configured values, in the order they were configured.</returns>
    protected IReadOnlyList<string> ConfiguredTextList(string setting)
    {
        if (Configuration is not { } configuration ||
            configuration.ValueKind is not JsonValueKind.Object ||
            !configuration.TryGetProperty(setting, out var value) ||
            value.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. value
                .EnumerateArray()
                .Where(entry => entry.ValueKind is JsonValueKind.String)
                .Select(entry => entry.GetString()!),
        ];
    }

    /// <summary>Raises the change, remembering what was written so it does not read back as external.</summary>
    /// <param name="json">The rewritten stored value, or empty to clear the slot.</param>
    protected Task WriteAsync(string json)
    {
        LastWritten = json;
        Value = json;

        return ValueChanged.InvokeAsync(json);
    }

    /// <summary>Rewrites the <c>value</c> member from text and raises the change.</summary>
    /// <param name="text">What the control holds; empty clears the slot.</param>
    protected Task WriteTextAsync(string? text) =>
        WriteAsync(StoredValue.WriteText(Value, FieldTypeKey, text));

    /// <summary>
    /// The rich-text format a value declares, defaulting to markdown.
    /// </summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <returns>Either <c>markdown</c> or <c>html</c>.</returns>
    /// <remarks>
    /// Shared by the rich-text editor and by the plain fallback, because both have to preserve it: a
    /// save that dropped the format would make the stored text uninterpretable, and one that guessed
    /// would reinterpret an author's HTML as markdown.
    /// </remarks>
    public static string FormatOf(string? json) =>
        StoredValue.ReadText(json, RichTextFormats.Member) is { Length: > 0 } format &&
        format is RichTextFormats.Markdown or RichTextFormats.Html
            ? format
            : RichTextFormats.Markdown;

    private static int? ReadInt32(JsonElement? configuration, string setting) =>
        configuration is { } element &&
        element.ValueKind is JsonValueKind.Object &&
        element.TryGetProperty(setting, out var value) &&
        value.ValueKind is JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;
}

/// <summary>
/// How a <see cref="FieldTypeKeys.RichText"/> value's text is written.
/// </summary>
/// <remarks>
/// The format travels in the payload rather than in configuration because it describes the value
/// that was written: a property switched from markdown to HTML must still be able to read what is
/// already stored. These strings mirror the constants on <c>RichTextFieldType</c>, which the
/// backoffice cannot reference — <c>Core</c> is not loaded in the browser.
/// </remarks>
public static class RichTextFormats
{
    /// <summary>The member naming how the stored value is written.</summary>
    public const string Member = "format";

    /// <summary>Markdown source, converted and sanitized on the way to the page.</summary>
    public const string Markdown = "markdown";

    /// <summary>HTML, sanitized on write and again on render.</summary>
    public const string Html = "html";
}
