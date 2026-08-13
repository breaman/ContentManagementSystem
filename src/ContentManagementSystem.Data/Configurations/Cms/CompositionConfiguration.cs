using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class CompositionConfiguration : IEntityTypeConfiguration<Composition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Composition> builder)
    {
        builder.Property(c => c.Key)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(c => c.Name)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasMaxLength(FieldLengths.ShortDescription);

        builder.HasIndex(c => c.Key)
            .IsUnique();

        builder.HasMany(c => c.Properties)
            .WithOne(p => p.Composition)
            .HasForeignKey(p => p.CompositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
