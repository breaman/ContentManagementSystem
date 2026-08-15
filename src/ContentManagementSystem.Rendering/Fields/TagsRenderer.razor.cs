using System.Text.Json;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>tags</c> value (spec section 7.1).
/// </summary>
/// <remarks>
/// The stored values are the tag text itself, not ids, so they render directly with no lookup. The
/// <c>Tag</c> and <c>PageTag</c> rows P8 introduces are a projection built from these on save, which
/// means a tag list renders correctly on a deployment where that projection has never been built —
/// and it is why this renderer needs nothing from a database.
/// <para>
/// Linking each tag to its listing page is P8's, when there is a listing page to link to. Emitting a
/// link now would be a dead <c>href</c> on every page carrying tags.
/// </para>
/// <para>
/// Unlike <c>choice</c>, the value is a list and only a list: the field type has no single-value
/// shape to fall back to.
/// </para>
/// </remarks>
public partial class TagsRenderer : CmsFieldRendererBase
{
    /// <summary>The stored tags in the order they were authored; empty when there are none.</summary>
    protected IReadOnlyList<string> Tags { get; private set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet() =>
        Tags = ArrayMember(ValueMember) is { } tags
            ? [.. tags.EnumerateArray()
                .Where(tag => tag.ValueKind is JsonValueKind.String)
                .Select(tag => tag.GetString()!)
                .Where(tag => tag.Length > 0)]
            : [];
}
