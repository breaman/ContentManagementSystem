using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.Property(c => c.Body)
            .HasMaxLength(FieldLengths.CommentBody)
            .IsRequired();

        builder.Property(c => c.ZoneKey)
            .HasMaxLength(FieldLengths.ContentKey);

        builder.Property(c => c.ResolvedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        // Every read of this table is "the thread on this page", oldest first.
        builder.HasIndex(c => new { c.PageId, c.Id });

        builder.HasOne(c => c.Page)
            .WithMany()
            .HasForeignKey(c => c.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.PageVersion)
            .WithMany()
            .HasForeignKey(c => c.PageVersionId)
            // Nulled rather than cascaded when a version is pruned: the remark still applies to the
            // page even after the version it was made against has gone.
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            // Self-referencing cascades are refused by SQL Server outright; deleting a thread root
            // is the comment service's job, one level at a time.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ResolvedBy)
            .WithMany()
            .HasForeignKey(c => c.ResolvedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
