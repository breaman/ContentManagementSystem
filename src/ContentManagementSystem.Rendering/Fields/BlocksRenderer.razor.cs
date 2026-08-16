using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Fields.Types;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>blocks</c> value: an ordered list of block instances, each through the component its
/// block type declares (spec sections 7.1 and 8.2).
/// </summary>
/// <remarks>
/// The second half of the indirection <see cref="CmsZone"/> starts. A zone resolves to this
/// renderer, and this resolves each item's stored <c>blockTypeKey</c> to a component through the
/// same scan the reconciler runs — so the template never learns what blocks a page contains, and
/// adding a block type is a deployment rather than a template change.
/// <para>
/// Every item is judged on its own. A block whose type this deployment no longer carries is skipped
/// and logged, and the blocks around it still render — the list is content, and one retired block
/// type must not blank a page (spec section 15.3).
/// </para>
/// <para>
/// The block type's captured schema is resolved once per block and cascaded, so a
/// <see cref="CmsBlockProperty"/> inside it can hand a renderer the configuration its property was
/// authored against without a lookup of its own. Resolution can fail, and a null schema is a
/// rendering condition rather than an error: the block's values still carry their own field type
/// discriminators, so only the configuration is lost.
/// </para>
/// </remarks>
public partial class BlocksRenderer : CmsFieldRendererBase
{
    [Inject]
    private ICmsComponentCatalog Components { get; set; } = default!;

    [Inject]
    private IContentSchemaCatalog Schemas { get; set; } = default!;

    [Inject]
    private ILogger<BlocksRenderer> Logger { get; set; } = default!;

    /// <summary>The blocks that resolved to a component, in the order they were authored.</summary>
    protected IReadOnlyList<PlannedBlock> Blocks { get; private set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        Blocks = [];

        if (ArrayMember(BlocksFieldType.ItemsMember) is not { } items) return;

        var planned = new List<PlannedBlock>(items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            if (Plan(item) is { } block)
            {
                planned.Add(block);
            }
        }

        Blocks = planned;
    }

    private PlannedBlock? Plan(JsonElement item)
    {
        if (item.ValueKind is not JsonValueKind.Object) return null;

        var blockTypeKey = ReadString(item, BlocksFieldType.BlockTypeKeyMember);

        if (blockTypeKey is not { Length: > 0 })
        {
            Logger.LogWarning(
                "A block in '{PropertyKey}' on page {PageId} version {VersionId} names no block " +
                "type, so nothing can say how to render it.",
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId);

            return null;
        }

        if (!Components.TryGetBlockType(blockTypeKey, out var componentType))
        {
            Logger.LogWarning(
                "No component declares block type '{BlockTypeKey}' (in '{PropertyKey}', page " +
                "{PageId}, version {VersionId}); the block renders nothing and the rest of the list " +
                "is unaffected.",
                blockTypeKey,
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId);

            return null;
        }

        var revision = ReadInt(item, BlocksFieldType.BlockTypeRevisionMember);

        var context = new BlockRenderContext(
            ReadGuid(item, BlocksFieldType.IdMember),
            blockTypeKey,
            revision,
            ReadObject(item, BlocksFieldType.PropertiesMember),
            Schemas.TryGetBlockType(blockTypeKey, revision, out var schema) ? schema : null);

        return new PlannedBlock(context, componentType, BlockParameters.For(componentType, context));
    }

    private static string? ReadString(JsonElement item, string member) =>
        item.TryGetProperty(member, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement item, string member) =>
        item.TryGetProperty(member, out var value) &&
        value.ValueKind is JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    /// <summary>
    /// Reads the block's id, falling back to a fresh one so the render key is still unique.
    /// </summary>
    /// <remarks>
    /// A block with no readable id cannot have come from this system, but it can still be rendered —
    /// the id is what keeps a re-order from rewriting markup, not what makes the block readable.
    /// Reusing a single sentinel for every such block would collide their keys and make Blazor treat
    /// two different blocks as one.
    /// </remarks>
    private static Guid ReadGuid(JsonElement item, string member) =>
        ReadString(item, member) is { } text && Guid.TryParse(text, out var id) ? id : Guid.NewGuid();

    private static JsonElement ReadObject(JsonElement item, string member) =>
        item.TryGetProperty(member, out var value) && value.ValueKind is JsonValueKind.Object
            ? value
            : default;

    /// <summary>One block instance, resolved to the component and parameters that render it.</summary>
    /// <param name="Context">The block's identity, properties, and captured schema.</param>
    /// <param name="ComponentType">The component declaring its block type key.</param>
    /// <param name="Parameters">The parameters to hand that component.</param>
    protected sealed record PlannedBlock(
        BlockRenderContext Context,
        Type ComponentType,
        Dictionary<string, object?> Parameters);
}
