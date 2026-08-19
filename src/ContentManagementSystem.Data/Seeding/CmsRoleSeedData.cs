using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Data.Seeding;

/// <summary>
/// The seven roles of spec section 3.2, seeded with the schema that holds them (task P7-01).
/// </summary>
/// <remarks>
/// Seeded through <c>HasData</c> for the same reason as <see cref="CmsSeedData"/>: the rows arrive
/// in the same transaction as the tables, so there is no window in which the application is running
/// against a database whose roles do not exist yet. A startup routine would have that window on
/// every deployment, and the failure it produces — an administrator whose role claim matches no row
/// — looks like a permissions bug rather than a missing seed.
/// <para>
/// <strong>Ids are fixed and are part of the contract.</strong> A <c>PageAcl</c> naming a role stores
/// its integer id, so renumbering these would silently repoint every role-scoped access rule at a
/// different role. Add new roles with new ids; never reorder.
/// </para>
/// <para>
/// Every value is deterministic, including <c>ConcurrencyStamp</c> — a generated GUID there changes
/// the model snapshot on each build and EF then reports a pending model change forever.
/// </para>
/// </remarks>
public static class CmsRoleSeedData
{
    /// <summary>Everything, including user and role management.</summary>
    public const int AdministratorId = 1;

    /// <summary>Manages templates, block types, and field type registration.</summary>
    public const int DeveloperId = 2;

    /// <summary>Full CRUD on pages and media within permitted sections; may publish.</summary>
    public const int EditorId = 3;

    /// <summary>Creates and edits pages but cannot publish.</summary>
    public const int AuthorId = 4;

    /// <summary>Reviews, approves, rejects, publishes, and schedules.</summary>
    public const int ApproverId = 5;

    /// <summary>Full media library management, including permanent deletion.</summary>
    public const int MediaManagerId = 6;

    /// <summary>Read-only backoffice access, including preview of drafts.</summary>
    public const int ViewerId = 7;

    /// <summary>The seven rows, in id order.</summary>
    public static IReadOnlyList<Role> Roles { get; } =
    [
        Create(AdministratorId, CmsRoles.Administrator, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0001"),
        Create(DeveloperId, CmsRoles.Developer, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0002"),
        Create(EditorId, CmsRoles.Editor, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0003"),
        Create(AuthorId, CmsRoles.Author, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0004"),
        Create(ApproverId, CmsRoles.Approver, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0005"),
        Create(MediaManagerId, CmsRoles.MediaManager, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0006"),
        Create(ViewerId, CmsRoles.Viewer, "9a1f0b64-0f21-4b3c-9a0e-6c1f5f0a0007"),
    ];

    /// <summary>The id the role of a given name was seeded with.</summary>
    /// <param name="roleName">One of the <see cref="CmsRoles"/> constants.</param>
    /// <returns>The seeded id, or null for a role this seed does not define.</returns>
    /// <remarks>
    /// The <c>PageAcl</c> resolver needs it: a caller's principal carries role <em>names</em>, and a
    /// rule naming a role stores its id. Reading the identity tables on every access check to
    /// translate between the two would be a join per decision.
    /// </remarks>
    public static int? IdFor(string roleName)
    {
        for (var i = 0; i < Roles.Count; i++)
        {
            if (string.Equals(Roles[i].Name, roleName, StringComparison.Ordinal)) return Roles[i].Id;
        }

        return null;
    }

    private static Role Create(int id, string name, string concurrencyStamp) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = concurrencyStamp,
    };
}
