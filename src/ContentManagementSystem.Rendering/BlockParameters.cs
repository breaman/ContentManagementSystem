using System.Buffers.Binary;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// The parameters a block component is handed, decided in one place.
/// </summary>
/// <remarks>
/// Two things render a block: the <c>blocks</c> field renderer, item by item, and the reusable
/// content renderer, which renders one item as the block type it is shaped by. Both hand the
/// component the same three facts, and a second copy of the rule below would eventually pass a
/// parameter one of them does not declare — which <c>DynamicComponent</c> answers with an exception
/// on a public request.
/// </remarks>
internal static class BlockParameters
{
    /// <summary>
    /// Builds the parameter set for a block component, when it is one that declares them.
    /// </summary>
    /// <param name="componentType">The component that will render the block.</param>
    /// <param name="context">The block's identity, properties, and captured schema.</param>
    /// <returns>Parameters for <c>DynamicComponent</c>.</returns>
    /// <remarks>
    /// A <c>[CmsBlockType]</c> attribute can sit on any component, and <c>DynamicComponent</c> throws
    /// when handed a parameter the target does not declare. Passing these only to components deriving
    /// from <see cref="CmsBlockBase"/> keeps that from turning a component written against a
    /// different base into an exception on a public request. Such a component still receives the
    /// cascading <see cref="BlockRenderContext"/>, since a cascading value is bound only where it is
    /// declared.
    /// </remarks>
    public static Dictionary<string, object?> For(Type componentType, BlockRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        ArgumentNullException.ThrowIfNull(context);

        return typeof(CmsBlockBase).IsAssignableFrom(componentType)
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(CmsBlockBase.Properties)] = context.Properties,
                [nameof(CmsBlockBase.BlockId)] = context.BlockId,
                [nameof(CmsBlockBase.BlockTypeRevision)] = context.BlockTypeRevision ?? 0,
            }
            : [];
    }
}

/// <summary>
/// Block identities for content that is a block without having been authored as one.
/// </summary>
/// <remarks>
/// A block inside a zone carries a stable GUID an editor's client generated (spec section 11.4). A
/// reusable item has no such id — it is one item, not one of a list — and yet it is rendered through
/// the same components, which read the id for diagnostics and use it as the render key.
/// </remarks>
internal static class BlockIds
{
    /// <summary>Namespace bytes, so a derived id cannot collide with an authored one by chance.</summary>
    private static readonly byte[] ReusableNamespace =
        [0x72, 0x75, 0x73, 0x62, 0x6C, 0x6F, 0x63, 0x6B, 0x00, 0x00, 0x00, 0x00];

    /// <summary>
    /// The id a reusable item's version renders under.
    /// </summary>
    /// <param name="versionId">The reusable version being rendered.</param>
    /// <returns>A stable identifier derived from that version.</returns>
    /// <remarks>
    /// Derived rather than fresh per render, and derived from the <em>version</em> rather than the
    /// item. Fresh would change the render key on every request, which is what tells Blazor two
    /// renders are the same subtree; the item alone would keep the key unchanged across a publish,
    /// which is precisely when the content beneath it has been replaced.
    /// </remarks>
    public static Guid ForReusableVersion(int versionId)
    {
        Span<byte> bytes = stackalloc byte[16];

        ReusableNamespace.CopyTo(bytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes[12..], versionId);

        return new Guid(bytes);
    }
}
