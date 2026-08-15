using System.Diagnostics.CodeAnalysis;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Maps a field type key to the component that renders values of that type
/// (spec section 15.2, ADR-0014).
/// </summary>
/// <remarks>
/// The seam exists here, in the infrastructure, because <see cref="CmsZone"/> and
/// <see cref="CmsBlockProperty"/> are what dispatch through it.
/// <para>
/// <c>IFieldType.RendererComponent</c> is <c>Type?</c> and the built-in field types answer null:
/// <c>Core</c> sits below <c>Rendering</c> in the reference graph and cannot name a component, so
/// the mapping is the hosting layer's to own (ADR-0014).
/// </para>
/// </remarks>
public interface IFieldRendererCatalog
{
    /// <summary>Every field type key that has a renderer.</summary>
    IReadOnlyCollection<string> FieldTypeKeys { get; }

    /// <summary>
    /// The registered field types that resolve to no renderer at all.
    /// </summary>
    /// <remarks>
    /// Read at startup and reported, because that is the only moment the omission is visible before
    /// a reader finds it. At render time the condition is indistinguishable from an unknown field
    /// type key and is treated the same way — nothing rendered, a warning logged — but by then the
    /// page is already missing content that nobody was told about (spec section 15.3).
    /// </remarks>
    IReadOnlyCollection<string> FieldTypesWithNoRenderer { get; }

    /// <summary>Finds the renderer for a field type.</summary>
    /// <param name="fieldTypeKey">The field type key stored on the value, such as <c>richText</c>.</param>
    /// <param name="componentType">The renderer component, when one is registered.</param>
    /// <returns><see langword="true"/> when a renderer is registered for the key.</returns>
    /// <remarks>
    /// Reports failure rather than throwing. A payload naming a field type this deployment no longer
    /// carries renders nothing and logs a warning — never an exception on a public request
    /// (spec section 15.3).
    /// </remarks>
    bool TryGetRenderer(string fieldTypeKey, [NotNullWhen(true)] out Type? componentType);
}
