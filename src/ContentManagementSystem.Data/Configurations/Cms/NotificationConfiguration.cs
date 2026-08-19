using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.Kind)
            .HasConversion<int>();

        builder.Property(n => n.Subject)
            .HasMaxLength(FieldLengths.ContentTitle)
            .IsRequired();

        builder.Property(n => n.Body)
            .HasMaxLength(FieldLengths.CommentBody)
            .IsRequired();

        builder.Property(n => n.Link)
            .HasMaxLength(FieldLengths.Url);

        builder.Property(n => n.CreatedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        // The inbox: one user's unread items, newest first. Id descending stands in for time —
        // the two agree, and the key is already there.
        builder.HasIndex(n => new { n.UserId, n.ReadOn, n.Id });

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            // A deleted user's inbox has nobody to read it.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.Page)
            .WithMany()
            .HasForeignKey(n => n.PageId)
            // Nulled rather than cascaded: "your scheduled publish failed" is still worth reading
            // after somebody deleted the page, and is in fact more worth reading then.
            .OnDelete(DeleteBehavior.SetNull);
    }
}
