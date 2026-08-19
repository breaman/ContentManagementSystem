using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class PageAclConfiguration : IEntityTypeConfiguration<PageAcl>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PageAcl> builder)
    {
        builder.Property(a => a.Permission)
            .HasMaxLength(FieldLengths.PermissionName)
            .IsRequired();

        builder.Property(a => a.PrincipalType)
            .HasConversion<int>();

        // One rule per principal per permission per page. Without this a principal can hold an
        // allow and a deny on the same page, and the resolution rules — which break ties by depth
        // and then by allow-versus-deny — have nothing left to break the tie with.
        //
        // It doubles as the resolver's covering index: the resolver asks for every rule on the
        // ancestors of one page, so leading on PageId is what it needs, and carrying the two
        // remaining columns keeps the lookup off the base table. A second index leading with PageId
        // would be the same index twice.
        builder.HasIndex(a => new { a.PageId, a.PrincipalType, a.PrincipalId, a.Permission })
            .IsUnique()
            .IncludeProperties(a => new { a.IsAllow, a.IsInherited })
            .HasDatabaseName("UX_PageAcls_PageId_Principal_Permission");

        builder.HasOne(a => a.Page)
            .WithMany()
            .HasForeignKey(a => a.PageId)
            // A permanently deleted page cannot be the subject of an access rule, and a rule that
            // outlived its page would be an orphan nothing ever reads.
            .OnDelete(DeleteBehavior.Cascade);

        // No foreign key on PrincipalId: it points at Users or Roles depending on PrincipalType.
        // See PageAcl for why that is preferred to two nullable keys.
    }
}
