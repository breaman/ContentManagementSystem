using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class BlockTypePropertyConfiguration : IEntityTypeConfiguration<BlockTypeProperty>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BlockTypeProperty> builder)
    {
        builder.Property(p => p.Key)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(p => p.Name)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasMaxLength(FieldLengths.ShortDescription);

        builder.Property(p => p.FieldTypeKey)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(p => p.ConfigurationJson)
            .HasColumnType(ColumnTypes.Json);

        builder.Property(p => p.Group)
            .HasMaxLength(FieldLengths.GroupName);

        builder.HasIndex(p => new { p.BlockTypeId, p.Key })
            .IsUnique();

        builder.HasData(CmsSeedData.RawHtmlContentProperty);
    }
}
