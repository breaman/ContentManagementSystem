using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class BlockTypeRevisionConfiguration : IEntityTypeConfiguration<BlockTypeRevision>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BlockTypeRevision> builder)
    {
        builder.Property(r => r.PropertySnapshotJson)
            .HasColumnType(ColumnTypes.Json)
            .IsRequired();

        builder.Property(r => r.Notes)
            .HasMaxLength(FieldLengths.RevisionNotes);

        builder.HasIndex(r => new { r.BlockTypeId, r.RevisionNumber })
            .IsUnique();

        builder.HasData(CmsSeedData.RawHtmlBlockTypeRevision);
    }
}
