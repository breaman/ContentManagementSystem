using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.Property(z => z.Key)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(z => z.Name)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(z => z.Description)
            .HasMaxLength(FieldLengths.ShortDescription);

        builder.Property(z => z.FieldTypeKey)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(z => z.ConfigurationJson)
            .HasColumnType(ColumnTypes.Json);

        builder.Property(z => z.Group)
            .HasMaxLength(FieldLengths.GroupName);

        // Zone keys address values inside the payload's zone dictionary, so they must be unique
        // within a template. They are global only to that template — two templates may both have a
        // "body" zone of different types.
        builder.HasIndex(z => new { z.TemplateId, z.Key })
            .IsUnique();
    }
}
