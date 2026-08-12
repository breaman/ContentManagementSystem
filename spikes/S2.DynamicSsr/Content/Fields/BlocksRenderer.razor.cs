using System.Text.Json;
using Microsoft.AspNetCore.Components;
using S2.DynamicSsr.Cms;

namespace S2.DynamicSsr.Content.Fields;

/// <summary>Renders a <c>blocks</c> value: resolves each block instance to its component.</summary>
public partial class BlocksRenderer : CmsFieldRendererBase
{
    [Inject]
    private BlockTypeRegistry BlockTypes { get; set; } = default!;

    [Inject]
    private RenderDiagnostics Diagnostics { get; set; } = default!;

    [Inject]
    private ILogger<BlocksRenderer> Logger { get; set; } = default!;

    /// <summary>A block instance paired with the component that renders it.</summary>
    private sealed record ResolvedBlock(string Id, Type Component, Dictionary<string, object?> Parameters);

    private IEnumerable<ResolvedBlock> ResolveBlocks()
    {
        if (!Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("blockTypeKey", out var key) ||
                key.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var blockTypeKey = key.GetString()!;

            // Spec §15.3: an unrecognised block type is skipped with a warning, never thrown.
            if (!BlockTypes.TryResolve(blockTypeKey, out var component))
            {
                Logger.LogWarning(
                    "No component is registered for block type '{BlockTypeKey}' (zone '{ZoneKey}', page {PageId}).",
                    blockTypeKey,
                    ZoneKey,
                    Context.PageId);
                Diagnostics.Record($"blockType.unknown key={blockTypeKey} zone={ZoneKey} page={Context.PageId}");

                continue;
            }

            var id = item.TryGetProperty("id", out var blockId) && blockId.ValueKind == JsonValueKind.String
                ? blockId.GetString()!
                : Guid.NewGuid().ToString();

            var properties = item.TryGetProperty("properties", out var props) ? props : default;

            yield return new ResolvedBlock(
                id,
                component,
                new Dictionary<string, object?> { ["Properties"] = properties });
        }
    }
}
