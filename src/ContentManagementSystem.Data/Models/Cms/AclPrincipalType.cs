namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// Whether an access rule names one person or a whole role (spec section 21.2).
/// </summary>
/// <remarks>
/// Stored as the discriminator half of a <c>(PrincipalType, PrincipalId)</c> pair rather than as two
/// nullable foreign keys. Two nullable keys admit a row that names both and a row that names
/// neither, and every reader would then have to decide what either means.
/// </remarks>
public enum AclPrincipalType
{
    /// <summary>The rule names a single <see cref="User"/> by id.</summary>
    User = 0,

    /// <summary>The rule names a <see cref="Role"/> by id, and reaches everyone holding it.</summary>
    Role = 1,
}
