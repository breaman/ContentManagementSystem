namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// The CMS roles from spec section 3.2, as ASP.NET Identity role names.
/// </summary>
/// <remarks>
/// Roles are additive: a user holding several gets the union of their permissions.
/// </remarks>
public static class CmsRoles
{
    /// <summary>
    /// The roles that may change the content model, as an <c>[Authorize(Roles = …)]</c> list.
    /// </summary>
    /// <remarks>
    /// A convenience for the backoffice screens, which run in WebAssembly where the server's
    /// permission policies do not exist — the client can only check roles, and the authoritative
    /// check is the one the service layer makes on every call anyway. Kept beside the role names so
    /// it cannot fall out of step with <c>CmsPermissionMap</c>'s entry for
    /// <see cref="CmsPermissions.StructureEdit"/>.
    /// </remarks>
    public const string StructureEditors = $"{Administrator},{Developer}";

    /// <summary>Everything, including user and role management.</summary>
    public const string Administrator = "Administrator";

    /// <summary>Manages templates, block types, and reusable-content types. Full content access.</summary>
    public const string Developer = "Developer";

    /// <summary>Full CRUD on pages and media within permitted sections; may publish.</summary>
    public const string Editor = "Editor";

    /// <summary>Creates and edits pages but cannot publish.</summary>
    public const string Author = "Author";

    /// <summary>Reviews, approves, rejects, publishes, and schedules.</summary>
    public const string Approver = "Approver";

    /// <summary>Full media library management, including permanent deletion.</summary>
    public const string MediaManager = "MediaManager";

    /// <summary>Read-only backoffice access, including preview of drafts.</summary>
    public const string Viewer = "Viewer";
}
