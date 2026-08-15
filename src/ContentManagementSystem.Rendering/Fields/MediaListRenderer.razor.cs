using System.Text.Json;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>mediaList</c> value — an ordered gallery (spec section 7.1).
/// </summary>
/// <remarks>
/// A list item has the same shape as a whole <c>media</c> value minus the discriminator, so each one
/// is handed to <see cref="MediaRenderer"/> rather than read out again here. That is also what puts
/// each item's <c>media:{id}</c> cache tag on the render without this class knowing about cache tags
/// at all.
/// <para>
/// Order is the author's and is emitted as written: a gallery is a sequence, and sorting it by
/// anything would silently discard a decision somebody made in the editor.
/// </para>
/// </remarks>
public partial class MediaListRenderer : CmsFieldRendererBase
{
    /// <summary>The picked items, in the order they were authored; empty when there are none.</summary>
    protected IReadOnlyList<JsonElement> Items { get; private set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet() =>
        Items = ArrayMember(ItemsMember) is { } items
            ? [.. items.EnumerateArray().Where(item => item.ValueKind is JsonValueKind.Object)]
            : [];
}
