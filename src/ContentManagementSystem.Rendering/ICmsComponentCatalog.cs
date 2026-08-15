using System.Diagnostics.CodeAnalysis;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Turns the keys stored in a payload back into the components that render them
/// (spec section 15.2).
/// </summary>
/// <remarks>
/// The half of the rendering pipeline that is pure lookup: a page version stores a
/// <c>templateKey</c> and every block stores a <c>blockTypeKey</c>, and nothing is switched on
/// statically anywhere.
/// <para>
/// Lookups report failure rather than throwing. A key no deployed component declares is an expected
/// state, not a fault — the reconciler marks the row orphaned, the <c>cms-templates</c> health check
/// degrades, and delivery renders the fallback for that row of spec section 15.3.
/// </para>
/// </remarks>
public interface ICmsComponentCatalog
{
    /// <summary>Every template key a deployed component declares.</summary>
    IReadOnlyCollection<string> TemplateKeys { get; }

    /// <summary>Every block type key a deployed component declares.</summary>
    IReadOnlyCollection<string> BlockTypeKeys { get; }

    /// <summary>Finds the component that renders pages on a template.</summary>
    /// <param name="templateKey">The stored template key.</param>
    /// <param name="componentType">The component type, when one is declared.</param>
    /// <returns><see langword="true"/> when a deployed component declares the key.</returns>
    bool TryGetTemplate(string templateKey, [NotNullWhen(true)] out Type? componentType);

    /// <summary>Finds the component that renders blocks of a type.</summary>
    /// <param name="blockTypeKey">The stored block type key.</param>
    /// <param name="componentType">The component type, when one is declared.</param>
    /// <returns><see langword="true"/> when a deployed component declares the key.</returns>
    bool TryGetBlockType(string blockTypeKey, [NotNullWhen(true)] out Type? componentType);
}
