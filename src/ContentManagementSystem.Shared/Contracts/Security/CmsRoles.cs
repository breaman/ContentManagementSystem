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

    /// <summary>
    /// The roles that may open the backoffice's content screens, as an <c>[Authorize(Roles = …)]</c>
    /// list.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="CmsPermissions.ContentRead"/>. See <see cref="StructureEditors"/> for why
    /// these lists exist at all and why the authoritative check is elsewhere.
    /// </remarks>
    public const string ContentReaders =
        $"{Administrator},{Developer},{Editor},{Author},{Approver},{Viewer}";

    /// <summary>The roles that may create and edit drafts. Mirrors <see cref="CmsPermissions.ContentEdit"/>.</summary>
    public const string ContentEditors = $"{Administrator},{Developer},{Editor},{Author},{Approver}";

    /// <summary>The roles that may publish. Mirrors <see cref="CmsPermissions.ContentPublish"/>.</summary>
    public const string ContentPublishers = $"{Administrator},{Developer},{Editor},{Approver}";

    /// <summary>
    /// The roles that may move content to the recycle bin and restore it. Mirrors
    /// <see cref="CmsPermissions.ContentDelete"/>.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="ContentEditors"/> by two roles, which is the point: an author may
    /// write anything and remove nothing, so the delete control is absent from their screen rather
    /// than present and refused.
    /// </remarks>
    public const string ContentDeleters = $"{Administrator},{Developer},{Editor}";

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
