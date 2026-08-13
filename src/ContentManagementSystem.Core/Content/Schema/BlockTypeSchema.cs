namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// The property definitions of one block type revision.
/// </summary>
/// <remarks>
/// The block-level counterpart of <see cref="ContentSchema"/>, and captured for the same reason: a
/// block instance records the revision it was authored against, so a property added or removed today
/// cannot change how an already-published block validates or renders.
/// <para>
/// The properties include those inherited through compositions. Composition is resolved when the
/// revision is cut, not when a payload is walked — a block's stored properties are flat, and the
/// walk has no business knowing where each definition came from.
/// </para>
/// </remarks>
public sealed class BlockTypeSchema
{
    private readonly Dictionary<string, ContentPropertySchema> _byKey;

    /// <summary>
    /// Creates a block type revision's schema.
    /// </summary>
    /// <param name="blockTypeKey">Key of the block type.</param>
    /// <param name="revisionNumber">The revision number this schema is a snapshot of.</param>
    /// <param name="properties">The property definitions, in editor order, compositions included.</param>
    /// <exception cref="ArgumentException">Two properties share a key.</exception>
    public BlockTypeSchema(
        string blockTypeKey,
        int revisionNumber,
        IEnumerable<ContentPropertySchema> properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockTypeKey);
        ArgumentNullException.ThrowIfNull(properties);

        BlockTypeKey = blockTypeKey;
        RevisionNumber = revisionNumber;
        Properties = [.. properties];
        _byKey = new Dictionary<string, ContentPropertySchema>(Properties.Count, StringComparer.Ordinal);

        foreach (var property in Properties)
        {
            if (!_byKey.TryAdd(property.Key, property))
            {
                throw new ArgumentException(
                    $"Block type '{blockTypeKey}' revision {revisionNumber} declares property " +
                    $"'{property.Key}' twice.",
                    nameof(properties));
            }
        }
    }

    /// <summary>Key of the block type this is a revision of.</summary>
    public string BlockTypeKey { get; }

    /// <summary>The revision number, as captured in a block's <c>blockTypeRevision</c>.</summary>
    public int RevisionNumber { get; }

    /// <summary>The property definitions, in editor order.</summary>
    public IReadOnlyList<ContentPropertySchema> Properties { get; }

    /// <summary>Finds a property definition by key.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <returns>The property, or null when the revision does not declare one by that key.</returns>
    public ContentPropertySchema? FindProperty(string propertyKey) =>
        _byKey.GetValueOrDefault(propertyKey);

    /// <summary>Whether the revision declares a property by this key.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <returns><see langword="true"/> when the property is declared.</returns>
    public bool DeclaresProperty(string propertyKey) => _byKey.ContainsKey(propertyKey);
}
