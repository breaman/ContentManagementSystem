using System.Diagnostics.CodeAnalysis;

using ContentManagementSystem.Core.Structure;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering;

/// <inheritdoc />
/// <remarks>
/// Built once from the same scan the reconciler runs, so the render path and the database can never
/// disagree about which component owns a key.
/// <para>
/// <strong>A declared type that is not a component is refused at startup.</strong> Rendering it
/// would fail on the request that first reached that page, one page at a time, in production; the
/// attribute is on the wrong class and that is a deployment defect, not a content condition.
/// </para>
/// </remarks>
public sealed class CmsComponentCatalog : ICmsComponentCatalog
{
    private readonly Dictionary<string, Type> _templates;
    private readonly Dictionary<string, Type> _blockTypes;

    /// <summary>
    /// Builds the catalog from the assemblies registered for component scanning.
    /// </summary>
    /// <param name="scanner">The shared <c>[CmsTemplate]</c> / <c>[CmsBlockType]</c> scan.</param>
    /// <exception cref="InvalidOperationException">
    /// A declaring type is not a Razor component, or two components declare one key.
    /// </exception>
    public CmsComponentCatalog(CmsComponentScanner scanner)
        : this(Scan(scanner))
    {
    }

    /// <summary>
    /// Builds the catalog from declarations that have already been read.
    /// </summary>
    /// <param name="declarations">The declarations, from <see cref="CmsComponentScanner"/>.</param>
    /// <exception cref="InvalidOperationException">A declaring type is not a Razor component.</exception>
    public CmsComponentCatalog(CmsComponentDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        _templates = Build(declarations.Templates.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ComponentType,
            StringComparer.Ordinal), "template");

        _blockTypes = Build(declarations.BlockTypes.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ComponentType,
            StringComparer.Ordinal), "block type");
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> TemplateKeys => _templates.Keys;

    /// <inheritdoc />
    public IReadOnlyCollection<string> BlockTypeKeys => _blockTypes.Keys;

    /// <inheritdoc />
    public bool TryGetTemplate(string templateKey, [NotNullWhen(true)] out Type? componentType) =>
        TryGet(_templates, templateKey, out componentType);

    /// <inheritdoc />
    public bool TryGetBlockType(string blockTypeKey, [NotNullWhen(true)] out Type? componentType) =>
        TryGet(_blockTypes, blockTypeKey, out componentType);

    private static CmsComponentDeclarations Scan(CmsComponentScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);

        return scanner.Scan();
    }

    private static Dictionary<string, Type> Build(Dictionary<string, Type> declared, string noun)
    {
        foreach (var (key, type) in declared)
        {
            if (!typeof(IComponent).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"'{type.FullName}' declares the {noun} key '{key}' but is not a Razor component. " +
                    "The attribute belongs on the component that renders it.");
            }
        }

        return declared;
    }

    // Ordinal, and null-tolerant: the key comes out of a stored payload, where a missing member
    // reads as null and must resolve to "no component" rather than throw on the delivery path.
    private static bool TryGet(
        Dictionary<string, Type> catalog,
        string key,
        [NotNullWhen(true)] out Type? componentType)
    {
        componentType = null;

        return !string.IsNullOrEmpty(key) && catalog.TryGetValue(key, out componentType);
    }
}
