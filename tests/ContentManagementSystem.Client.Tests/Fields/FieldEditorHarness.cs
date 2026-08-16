using Bunit;

using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Tests.Fields;

/// <summary>
/// Renders one field editor against a slot, and records what it wrote.
/// </summary>
/// <remarks>
/// Every editor takes the same three parameters — that is what
/// <see cref="FieldEditorBase"/> is for — so one harness renders any of them, and a test says only
/// what is different about the one it is exercising: the field type, the configuration, and the
/// stored value.
/// </remarks>
public sealed class FieldEditorHarness : IDisposable
{
    /// <summary>The bUnit context, so a test can register the services its editor injects.</summary>
    public BunitContext Bunit { get; } = new();

    /// <summary>Every value the editor has written, in order.</summary>
    public List<string> Written { get; } = [];

    /// <summary>The last value the editor wrote, or null when it has written nothing.</summary>
    public string? Last => Written.Count > 0 ? Written[^1] : null;

    public void Dispose()
    {
        Bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Builds a captured slot, with configuration written as JSON.</summary>
    /// <param name="fieldTypeKey">The field type filling the slot.</param>
    /// <param name="configuration">The slot's configuration as a JSON object, or null.</param>
    /// <param name="key">The slot's payload key.</param>
    /// <param name="name">The slot's editor-facing name.</param>
    /// <param name="isRequired">Whether an empty value blocks publishing.</param>
    /// <returns>The slot.</returns>
    public static CapturedSlot Slot(
        string fieldTypeKey,
        string? configuration = null,
        string key = "zone",
        string name = "Zone",
        bool isRequired = false) =>
        new(
            key,
            name,
            fieldTypeKey,
            isRequired,
            SortOrder: 0,
            Configuration: configuration is null
                ? null
                : System.Text.Json.JsonDocument.Parse(configuration).RootElement.Clone());

    /// <summary>Builds the context a card would hand the editor.</summary>
    /// <param name="slot">The slot being drawn.</param>
    /// <param name="disabled">Whether the surrounding form is read-only.</param>
    /// <param name="diagnostics">What validation said, or null when nothing has been checked.</param>
    /// <returns>The context.</returns>
    public static FieldEditorContext Context(
        CapturedSlot slot,
        bool disabled = false,
        ZoneDiagnostics? diagnostics = null) =>
        new(
            slot,
            $"zone-{slot.Key}-control",
            $"zone-{slot.Key}-name",
            $"zone-{slot.Key}-help",
            disabled,
            (diagnostics ?? ZoneDiagnostics.Empty).Severity,
            diagnostics,
            $"zones.{slot.Key}");

    /// <summary>Renders an editor and records what it writes.</summary>
    /// <typeparam name="TEditor">The editor component.</typeparam>
    /// <param name="context">The context the card would hand it.</param>
    /// <param name="value">The stored value as JSON text.</param>
    /// <returns>The rendered component.</returns>
    public IRenderedComponent<TEditor> Render<TEditor>(FieldEditorContext context, string value = "")
        where TEditor : FieldEditorBase =>
        Bunit.Render<TEditor>(parameters => parameters
            .Add(p => p.Field, context)
            .Add(p => p.Value, value)
            .Add(p => p.ValueChanged, Written.Add));
}
