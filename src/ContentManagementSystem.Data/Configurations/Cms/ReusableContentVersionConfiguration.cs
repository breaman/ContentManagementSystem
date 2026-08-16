using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class ReusableContentVersionConfiguration : IEntityTypeConfiguration<ReusableContentVersion>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReusableContentVersion> builder)
    {
        builder.Property(version => version.Label)
            .HasMaxLength(FieldLengths.EntityName);

        builder.Property(version => version.ContentJson)
            .HasColumnType(ColumnTypes.Json)
            .IsRequired();

        // Stored as the underlying tinyint, exactly as a page version's is. The names are a UI
        // concern; the numbers are the contract, and they appear in the indexes below.
        builder.Property(version => version.Status)
            .HasConversion<byte>();

        builder.Property(version => version.RowVersion)
            .IsRowVersion();

        // A version number is what a pinned placement names (spec section 9.2), so two rows sharing
        // one within an item would make a pin ambiguous — and ambiguous in the worst direction, on
        // the delivery path, for content somebody pinned precisely because it must not change.
        // Unique rather than merely indexed because the number is computed from the current maximum,
        // and two concurrent publishes race for it.
        builder.HasIndex(version => new { version.ReusableContentId, version.VersionNumber })
            .IsUnique();

        // Version history and "find this item's draft" both filter by status within an item.
        builder.HasIndex(version => new { version.ReusableContentId, version.Status });

        // The scheduler's query: everything approved and due (spec section 11.6).
        builder.HasIndex(version => new { version.Status, version.PublishOn });

        builder.HasOne(version => version.ReusableContent)
            .WithMany(item => item.Versions)
            .HasForeignKey(version => version.ReusableContentId)
            // The other half of the mutual reference described on ReusableContentConfiguration.
            // Version history is the thing a soft delete exists to preserve, so cascading it away on
            // a hard delete of the item would defeat the point.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
