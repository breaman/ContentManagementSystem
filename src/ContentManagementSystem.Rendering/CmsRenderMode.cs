namespace ContentManagementSystem.Rendering;

/// <summary>
/// Which audience a render is for (spec section 15.2).
/// </summary>
/// <remarks>
/// The spec calls this type <c>RenderMode</c>. It is <c>CmsRenderMode</c> here because every
/// <c>.razor</c> file imports <c>Microsoft.AspNetCore.Components.Web</c>, which already has a
/// <c>RenderMode</c>: the name would be ambiguous in exactly the files that need it most. The S2
/// spike hit this and recommended the rename.
/// <para>
/// The distinction is not cosmetic. A link to an unpublished page resolves to that page's draft URL
/// and is badged under <see cref="Preview"/>, and resolves to nothing at all under
/// <see cref="Live"/> — collapsing the two would either leak a draft URL to an anonymous visitor or
/// make preview useless for walking an unreleased section (spec section 12.3).
/// </para>
/// </remarks>
public enum CmsRenderMode
{
    /// <summary>The anonymous public site. Only published content is reachable.</summary>
    Live = 0,

    /// <summary>An authenticated editor previewing a specific version (spec section 12.1).</summary>
    Preview = 1,

    /// <summary>
    /// A preview of the site as it will stand at a future moment, once scheduled publishes have run
    /// (spec section 12.1).
    /// </summary>
    ScheduledPreview = 2,
}
