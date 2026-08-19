using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Seeding;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations;

/// <summary>
/// Seeds the seven CMS roles onto the Identity role table (task P7-01).
/// </summary>
/// <remarks>
/// Everything else about <see cref="Role"/> — keys, lengths, the normalized-name index — comes from
/// <c>IdentityDbContext</c>'s own model building and is deliberately not restated here.
/// </remarks>
public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasData(CmsRoleSeedData.Roles);
    }
}
