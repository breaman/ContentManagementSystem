using System.Reflection;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// The assemblies searched for <c>[CmsTemplate]</c> and <c>[CmsBlockType]</c> declarations.
/// </summary>
/// <remarks>
/// Registered explicitly by the host rather than discovered from the loaded assembly list. Spec
/// section 8.4 says "scans loaded assemblies", and doing that literally has three problems worth
/// avoiding: the set is whatever the CLR happens to have faulted in by the time reconciliation runs,
/// it includes every framework and third-party assembly, and a trimmed or single-file publish does
/// not answer the question the same way. Naming the assemblies makes the scan deterministic and lets
/// a test reconcile against exactly its own fixtures.
/// </remarks>
public sealed class CmsStructureAssemblies
{
    /// <summary>
    /// Names the assemblies to scan.
    /// </summary>
    /// <param name="assemblies">The assemblies declaring templates and block types.</param>
    public CmsStructureAssemblies(params Assembly[] assemblies) =>
        Assemblies = assemblies.Distinct().ToList();

    /// <summary>The assemblies to scan, in the order given.</summary>
    public IReadOnlyList<Assembly> Assemblies { get; }
}
