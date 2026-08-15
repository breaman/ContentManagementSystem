using System.Diagnostics.CodeAnalysis;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Rendering.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering;

/// <inheritdoc />
/// <remarks>
/// Built from the field type registry rather than from the built-in table, which is what makes the
/// catalog describe <em>this deployment</em>: a field type nobody registered has no renderer here,
/// so content authored against a field type that has since been removed renders nothing and logs,
/// exactly as spec section 15.3 requires. A catalog seeded from the built-in table instead would
/// happily render values whose field type was deliberately taken out.
/// <para>
/// Each field type is asked first, and answered for only when it declines. A field type shipped in
/// an assembly that can see the component projects names its own renderer and needs no entry
/// anywhere; the built-in ones cannot, and are mapped by key (ADR-0014).
/// </para>
/// </remarks>
public sealed class FieldRendererCatalog : IFieldRendererCatalog
{
    private readonly Dictionary<string, Type> _renderers;

    private readonly string[] _missing;

    /// <summary>
    /// Builds the catalog over the field types this deployment registered.
    /// </summary>
    /// <param name="registry">The registered field types.</param>
    /// <exception cref="InvalidOperationException">
    /// A field type names a renderer that is not a Razor component.
    /// </exception>
    public FieldRendererCatalog(IFieldTypeRegistry registry)
        : this(registry?.All ?? throw new ArgumentNullException(nameof(registry)))
    {
    }

    private FieldRendererCatalog(IEnumerable<IFieldType> fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(fieldTypes);

        _renderers = new Dictionary<string, Type>(StringComparer.Ordinal);

        var missing = new List<string>();

        foreach (var fieldType in fieldTypes)
        {
            if (Resolve(fieldType) is { } renderer)
            {
                _renderers[fieldType.Key] = renderer;

                continue;
            }

            missing.Add(fieldType.Key);
        }

        missing.Sort(StringComparer.Ordinal);
        _missing = [.. missing];
    }

    /// <summary>
    /// Builds the catalog over an explicit set of field types.
    /// </summary>
    /// <param name="fieldTypes">The field types to map.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidOperationException">
    /// A field type names a renderer that is not a Razor component.
    /// </exception>
    /// <remarks>
    /// The rule separated from where the field types came from, so a test can state exactly which
    /// field types exist and still exercise the mapping a deployment runs.
    /// <para>
    /// A factory rather than a second public constructor, which is not a style preference: the
    /// container registers every field type as <see cref="IFieldType"/>, so a constructor taking
    /// <c>IEnumerable&lt;IFieldType&gt;</c> is as resolvable as the one taking the registry, neither
    /// is a superset of the other, and the service provider refuses the type as ambiguous — at
    /// startup, with a message about constructors rather than about renderers.
    /// </para>
    /// </remarks>
    public static FieldRendererCatalog For(params IFieldType[] fieldTypes) => new(fieldTypes);

    /// <inheritdoc />
    public IReadOnlyCollection<string> FieldTypeKeys => _renderers.Keys;

    /// <inheritdoc />
    public IReadOnlyCollection<string> FieldTypesWithNoRenderer => _missing;

    /// <inheritdoc />
    public bool TryGetRenderer(string fieldTypeKey, [NotNullWhen(true)] out Type? componentType)
    {
        componentType = null;

        return !string.IsNullOrEmpty(fieldTypeKey) && _renderers.TryGetValue(fieldTypeKey, out componentType);
    }

    /// <summary>
    /// Finds the renderer for one field type, preferring the one it names itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The named type is not a Razor component. Thrown rather than ignored, and at startup rather
    /// than on a request, for the reason <see cref="CmsComponentCatalog"/> refuses the same mistake:
    /// rendering it would fail one page at a time, in production, on whichever request first reached
    /// content using that field type.
    /// </exception>
    private static Type? Resolve(IFieldType fieldType)
    {
        if (fieldType.RendererComponent is not { } declared)
        {
            return BuiltInFieldRenderers.ByFieldTypeKey.GetValueOrDefault(fieldType.Key);
        }

        if (!typeof(IComponent).IsAssignableFrom(declared))
        {
            throw new InvalidOperationException(
                $"Field type '{fieldType.Key}' names '{declared.FullName}' as its renderer, but " +
                "that is not a Razor component.");
        }

        return declared;
    }
}
