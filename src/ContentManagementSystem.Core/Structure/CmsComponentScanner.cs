using System.Reflection;

using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Structure;

/// <summary>A template declared in code by a <c>[CmsTemplate]</c> component.</summary>
/// <param name="Attribute">The declaration itself.</param>
/// <param name="ComponentType">The Razor component that renders pages on this template.</param>
public sealed record CmsTemplateDeclaration(CmsTemplateAttribute Attribute, Type ComponentType)
{
    /// <summary>Assembly-qualified name, minus version, of the declaring component.</summary>
    public string ComponentTypeName => ComponentTypeNames.Of(ComponentType);
}

/// <summary>A block type declared in code by a <c>[CmsBlockType]</c> component.</summary>
/// <param name="Attribute">The declaration itself.</param>
/// <param name="ComponentType">The Razor component that renders blocks of this type.</param>
public sealed record CmsBlockTypeDeclaration(CmsBlockTypeAttribute Attribute, Type ComponentType)
{
    /// <summary>Assembly-qualified name, minus version, of the declaring component.</summary>
    public string ComponentTypeName => ComponentTypeNames.Of(ComponentType);
}

/// <summary>Everything one scan of the registered assemblies found, keyed by content key.</summary>
/// <param name="Templates">Template declarations by template key.</param>
/// <param name="BlockTypes">Block type declarations by block type key.</param>
public sealed record CmsComponentDeclarations(
    IReadOnlyDictionary<string, CmsTemplateDeclaration> Templates,
    IReadOnlyDictionary<string, CmsBlockTypeDeclaration> BlockTypes)
{
    /// <summary>An empty result, for hosts that register no component assemblies at all.</summary>
    public static CmsComponentDeclarations Empty { get; } = new(
        new Dictionary<string, CmsTemplateDeclaration>(StringComparer.Ordinal),
        new Dictionary<string, CmsBlockTypeDeclaration>(StringComparer.Ordinal));
}

/// <summary>
/// Finds the templates and block types declared by <c>[CmsTemplate]</c> and <c>[CmsBlockType]</c>
/// components in the registered assemblies (spec section 8.4).
/// </summary>
/// <remarks>
/// One scan, two consumers, deliberately. <c>TemplateReconciler</c> asks what code declares so it can
/// create and orphan rows; the rendering pipeline asks the same question so it can turn a stored
/// <c>templateKey</c> back into a component. A second scan written for the second consumer would be a
/// second place for the duplicate-key rule to live, and the copy that drifts is the one nobody wrote
/// a test for — the render path would then happily pick a winner the reconciler refused to.
/// </remarks>
/// <param name="assemblies">The assemblies declaring templates and block types.</param>
/// <param name="logger">Log for an assembly whose types cannot all be loaded.</param>
public sealed class CmsComponentScanner(CmsStructureAssemblies assemblies, ILogger<CmsComponentScanner> logger)
{
    /// <summary>
    /// Scans the registered assemblies.
    /// </summary>
    /// <returns>The declarations found, keyed by content key.</returns>
    /// <exception cref="InvalidOperationException">
    /// Two components declare the same key. Left to throw at startup rather than resolved: two
    /// components claiming one key has no defensible winner, and picking one silently would render
    /// half a site with the wrong markup.
    /// </exception>
    public CmsComponentDeclarations Scan() => ScanTypes(assemblies.Assemblies.SelectMany(GetTypes));

    /// <summary>
    /// Reads the declarations off an explicit set of types.
    /// </summary>
    /// <param name="types">The types to inspect.</param>
    /// <returns>The declarations found, keyed by content key.</returns>
    /// <exception cref="InvalidOperationException">Two types declare the same key.</exception>
    /// <remarks>
    /// The rule itself, separated from where the types came from, so a host that resolves its
    /// components some other way — and a test that wants to state exactly which types exist — runs
    /// the same duplicate-key check a deployment does.
    /// </remarks>
    public static CmsComponentDeclarations ScanTypes(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var templates = new Dictionary<string, CmsTemplateDeclaration>(StringComparer.Ordinal);
        var blockTypes = new Dictionary<string, CmsBlockTypeDeclaration>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            if (type.GetCustomAttribute<CmsTemplateAttribute>() is { } template)
            {
                Add(templates, template.Key, new CmsTemplateDeclaration(template, type), type, "template");
            }

            if (type.GetCustomAttribute<CmsBlockTypeAttribute>() is { } blockType)
            {
                Add(blockTypes, blockType.Key, new CmsBlockTypeDeclaration(blockType, type), type, "block type");
            }
        }

        return new CmsComponentDeclarations(templates, blockTypes);
    }

    private static void Add<T>(
        Dictionary<string, T> declarations,
        string key,
        T declaration,
        Type type,
        string noun)
    {
        if (!declarations.TryAdd(key, declaration))
        {
            throw new InvalidOperationException(
                $"Two components declare the {noun} key '{key}'; '{type.FullName}' is the second. " +
                "Keys are written into stored payloads and must identify exactly one component.");
        }
    }

    /// <summary>Reads an assembly's types, tolerating one that cannot be fully loaded.</summary>
    /// <remarks>
    /// A <see cref="ReflectionTypeLoadException"/> here means one type in the assembly references
    /// something missing. Taking the loadable types and carrying on is right: the alternative is
    /// that an unrelated broken type stops every template in the assembly from being found, which
    /// turns a rendering bug into a site that cannot start.
    /// </remarks>
    private IEnumerable<Type> GetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            logger.LogWarning(
                exception,
                "Some types in {Assembly} could not be loaded; scanning the rest.",
                assembly.GetName().Name);

            return exception.Types.OfType<Type>();
        }
    }
}
