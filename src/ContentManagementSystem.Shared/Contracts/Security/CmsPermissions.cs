namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// The permission vocabulary of the CMS (spec section 20.4).
/// </summary>
/// <remarks>
/// One list, shared by the authorization policies the server registers and the service-layer checks
/// domain services make, so a policy and the check it is meant to back cannot end up naming two
/// different strings.
/// <para>
/// These are global grants. Narrowing a grant to a subtree is the job of section ACLs, which arrive
/// in Phase 7 and are applied in the service layer alongside these checks (spec section 21.2).
/// </para>
/// </remarks>
public static class CmsPermissions
{
    /// <summary>Read pages, reusable content, and their versions.</summary>
    public const string ContentRead = "Content.Read";

    /// <summary>Create and edit drafts.</summary>
    public const string ContentEdit = "Content.Edit";

    /// <summary>Publish, unpublish, and schedule.</summary>
    public const string ContentPublish = "Content.Publish";

    /// <summary>Soft-delete content and restore it.</summary>
    public const string ContentDelete = "Content.Delete";

    /// <summary>Upload media and edit its metadata.</summary>
    public const string MediaUpload = "Media.Upload";

    /// <summary>Delete media permanently.</summary>
    public const string MediaDelete = "Media.Delete";

    /// <summary>Manage templates, zones, block types, and compositions.</summary>
    public const string StructureEdit = "Structure.Edit";

    /// <summary>Manage site settings, redirects, and navigation.</summary>
    public const string SettingsEdit = "Settings.Edit";

    /// <summary>Manage users, roles, and ACLs.</summary>
    public const string UsersManage = "Users.Manage";
}
