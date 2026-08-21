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

    /// <summary>Submit a draft for review.</summary>
    /// <remarks>
    /// Held by the roles that write content, and — following the section 21.1 matrix — not by
    /// <c>Approver</c>, whose editing is confined to the items assigned to them.
    /// </remarks>
    public const string ContentSubmit = "Content.Submit";

    /// <summary>
    /// Approve or reject a submission.
    /// </summary>
    /// <remarks>
    /// Deliberately not the same permission as <see cref="ContentPublish"/>, though the two overlap.
    /// Section 21.1 gives <c>Editor</c> publish but not approve, which is the whole point of a
    /// two-step workflow: the person who may push the button is not automatically the person who may
    /// say it is ready.
    /// </remarks>
    public const string ContentApprove = "Content.Approve";

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

    /// <summary>Edit and publish the stylesheet the public site is rendered with.</summary>
    /// <remarks>
    /// Separate from <see cref="SettingsEdit"/> although the same two roles hold it today.
    /// Publishing CSS reaches every anonymous visitor immediately — there is no draft state on the
    /// public side and no approval step in front of it — which is a different kind of act from
    /// setting a retention window. Keeping it separate is also what lets a future designer role
    /// exist without being handed workflow mode and retention along with it (spec section 30, D27).
    /// </remarks>
    public const string AppearanceEdit = "Appearance.Edit";

    /// <summary>Manage users, roles, and ACLs.</summary>
    public const string UsersManage = "Users.Manage";

    /// <summary>Read the audit log.</summary>
    /// <remarks>
    /// Separate from <see cref="UsersManage"/> because section 21.1 gives it to <c>Developer</c> as
    /// well: diagnosing "what happened to this page" is development work, and requiring the ability
    /// to grant yourself roles in order to do it would be the wrong trade.
    /// </remarks>
    public const string AuditView = "Audit.View";
}
