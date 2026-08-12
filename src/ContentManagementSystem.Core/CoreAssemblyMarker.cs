namespace ContentManagementSystem.Core;

/// <summary>
/// Anchor type used to locate this assembly for reflection-based discovery.
/// </summary>
/// <remarks>
/// Startup registration scans assemblies for field types, template definitions, and block types
/// (see <c>TemplateReconciler</c>, spec section 8.4). Referencing a marker type keeps that scan
/// independent of any particular class staying in the assembly.
/// </remarks>
public static class CoreAssemblyMarker
{
    /// <summary>Gets the assembly containing the CMS domain services.</summary>
    public static System.Reflection.Assembly Assembly => typeof(CoreAssemblyMarker).Assembly;
}
