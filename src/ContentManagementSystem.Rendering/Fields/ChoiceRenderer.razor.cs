using System.Text.Json;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>choice</c> value, single or multiple (spec section 7.1).
/// </summary>
/// <remarks>
/// The stored value is the option's own text, so it is emitted as it stands — the field type keeps
/// no separate label, and inventing one here would mean the page and the editor disagreed about what
/// was picked.
/// <para>
/// The shape decides the markup, not the <c>multiple</c> setting. A property that was multiple
/// yesterday and is single today still has pages holding arrays, and reading the setting would
/// render those as nothing.
/// </para>
/// </remarks>
public partial class ChoiceRenderer : CmsFieldRendererBase
{
    /// <summary>The single selection, or null when the value is absent or holds a list.</summary>
    protected string? Single => ValueText;

    /// <summary>The selections when a list was stored; empty otherwise.</summary>
    protected IReadOnlyList<string> Selected { get; private set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet() =>
        Selected = ArrayMember(ValueMember) is { } options
            ? [.. options.EnumerateArray()
                .Where(option => option.ValueKind is JsonValueKind.String)
                .Select(option => option.GetString()!)]
            : [];
}
