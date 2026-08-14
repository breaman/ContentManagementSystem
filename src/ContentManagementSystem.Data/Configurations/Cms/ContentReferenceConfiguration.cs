using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class ContentReferenceConfiguration : IEntityTypeConfiguration<ContentReference>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContentReference> builder)
    {
        builder.Property(r => r.SourceType)
            .HasConversion<byte>();

        builder.Property(r => r.TargetType)
            .HasConversion<byte>();

        builder.Property(r => r.ZoneKey)
            .HasMaxLength(FieldLengths.ContentKey);

        builder.Property(r => r.PropertyKey)
            .HasMaxLength(FieldLengths.ContentKey);

        // "Where is this used?" — asked before every delete, every media replacement, and every
        // reusable-content publish. The hot one.
        builder.HasIndex(r => new { r.TargetType, r.TargetId });

        // The rebuild's own query: delete this version's rows, insert the new ones. Also what
        // computes a rendered page's cache tags.
        builder.HasIndex(r => new { r.SourceType, r.SourceVersionId });
    }
}
