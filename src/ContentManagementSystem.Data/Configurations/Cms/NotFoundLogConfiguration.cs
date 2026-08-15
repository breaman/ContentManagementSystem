using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class NotFoundLogConfiguration : IEntityTypeConfiguration<NotFoundLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotFoundLog> builder)
    {
        builder.Property(l => l.Url)
            .HasMaxLength(FieldLengths.Url)
            .IsRequired();

        builder.Property(l => l.UrlHash)
            .HasColumnType(ColumnTypes.Sha256Hash)
            .IsRequired();

        builder.Property(l => l.Referrer)
            .HasMaxLength(FieldLengths.Url);

        builder.Property(l => l.FirstSeenOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(l => l.LastSeenOn)
            .HasColumnType(ColumnTypes.Timestamp);

        // One row per URL. The uniqueness is what makes the writer an upsert rather than an append,
        // and it is the whole reason a crawler cannot make this the largest table on the site.
        builder.HasIndex(l => l.UrlHash)
            .IsUnique();

        // The report's own ordering: the URLs actually receiving traffic, worst first
        // (spec section 10.6).
        builder.HasIndex(l => l.HitCount)
            .IsDescending();
    }
}
