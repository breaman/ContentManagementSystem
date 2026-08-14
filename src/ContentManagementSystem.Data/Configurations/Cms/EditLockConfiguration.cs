using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class EditLockConfiguration : IEntityTypeConfiguration<EditLock>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EditLock> builder)
    {
        // At most one lock per page, expressed as the key rather than as a unique index over a
        // surrogate. The value supplied is the page's own id, so identity generation would only
        // offer a second, wrong way to create a row.
        builder.HasKey(l => l.PageId);

        builder.Property(l => l.PageId)
            .ValueGeneratedNever();

        builder.HasOne(l => l.Page)
            .WithMany()
            .HasForeignKey(l => l.PageId)
            // The one cascade in this schema. A lock is disposable UX state that regenerates the
            // moment somebody opens the editor again, and Restrict here would let a stale
            // heartbeat block a permanent delete that the recycle bin had already cleared.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // No query filter on the page's IsDeleted: the reaper and the recycle bin both need to see
        // locks on pages that have since been deleted, and a lock on a deleted page is meaningless
        // to everyone else anyway.
    }
}
