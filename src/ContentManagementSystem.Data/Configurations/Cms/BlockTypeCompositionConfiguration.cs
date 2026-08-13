using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class BlockTypeCompositionConfiguration : IEntityTypeConfiguration<BlockTypeComposition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BlockTypeComposition> builder)
    {
        // Composing the same group twice would flatten to duplicate property keys.
        builder.HasIndex(c => new { c.BlockTypeId, c.CompositionId })
            .IsUnique();

        builder.HasOne(c => c.BlockType)
            .WithMany(b => b.Compositions)
            .HasForeignKey(c => c.BlockTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Composition)
            .WithMany(c => c.BlockTypes)
            .HasForeignKey(c => c.CompositionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
