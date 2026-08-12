namespace ContentManagementSystem.Rendering;

/// <summary>
/// Anchor type used to locate this assembly for reflection-based discovery.
/// </summary>
/// <remarks>
/// Template and block-type components are found by scanning for <c>[CmsTemplate]</c> and
/// <c>[CmsBlockType]</c> (spec section 15.2). Referencing a marker type keeps that scan independent
/// of any particular component staying in the assembly.
/// </remarks>
public static class RenderingAssemblyMarker
{
    /// <summary>Gets the assembly containing the CMS rendering components.</summary>
    public static System.Reflection.Assembly Assembly => typeof(RenderingAssemblyMarker).Assembly;
}
