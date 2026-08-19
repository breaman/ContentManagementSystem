using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.Property(t => t.Name)
            .HasMaxLength(FieldLengths.Slug)
            .IsRequired();

        builder.Property(t => t.Slug)
            .HasMaxLength(FieldLengths.Slug)
            .IsRequired();

        // One row per label. Without this, "Product" and "product" become two tags that filter to
        // two different sets of pages and no editor can tell them apart in a picker.
        builder.HasIndex(t => t.Slug)
            .IsUnique();
    }
}
