using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Authorization;

/// <summary>
/// Which roles hold which permission (spec section 21.1).
/// </summary>
/// <remarks>
/// One table, read by two consumers that must never disagree: the authorization policies registered
/// at startup, and the request-scoped <see cref="HttpCmsAuthorization"/> that domain services ask.
/// If an endpoint policy admitted a role the service check then refused, the failure would surface
/// as an unexplained error deep in a save.
/// <para>
/// Grants here are global. Narrowing them to a subtree — and the "edit only while assigned for
/// review" qualifier on <see cref="CmsRoles.Approver"/> — is the job of section ACLs in Phase 7
/// (spec section 21.2).
/// </para>
/// </remarks>
public static class CmsPermissionMap
{
    /// <summary>Permission to the roles that hold it.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RolesByPermission { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [CmsPermissions.ContentRead] =
            [
                CmsRoles.Administrator, CmsRoles.Developer, CmsRoles.Editor, CmsRoles.Author,
                CmsRoles.Approver, CmsRoles.Viewer,
            ],
            [CmsPermissions.ContentEdit] =
            [
                CmsRoles.Administrator, CmsRoles.Developer, CmsRoles.Editor, CmsRoles.Author,
                CmsRoles.Approver,
            ],
            [CmsPermissions.ContentPublish] =
            [
                CmsRoles.Administrator, CmsRoles.Developer, CmsRoles.Editor, CmsRoles.Approver,
            ],
            [CmsPermissions.ContentDelete] =
            [
                CmsRoles.Administrator, CmsRoles.Developer, CmsRoles.Editor,
            ],
            [CmsPermissions.MediaUpload] =
            [
                CmsRoles.Administrator, CmsRoles.Developer, CmsRoles.Editor, CmsRoles.Author,
                CmsRoles.MediaManager,
            ],
            [CmsPermissions.MediaDelete] =
            [
                CmsRoles.Administrator, CmsRoles.MediaManager,
            ],
            [CmsPermissions.StructureEdit] =
            [
                CmsRoles.Administrator, CmsRoles.Developer,
            ],
            [CmsPermissions.SettingsEdit] =
            [
                CmsRoles.Administrator, CmsRoles.Developer,
            ],
            [CmsPermissions.UsersManage] =
            [
                CmsRoles.Administrator,
            ],
        };
}
