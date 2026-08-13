using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class BlockTypeConfiguration : IEntityTypeConfiguration<BlockType>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BlockType> builder)
    {
        builder.Property(b => b.Key)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(b => b.Name)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(b => b.Description)
            .HasMaxLength(FieldLengths.ShortDescription);

        builder.Property(b => b.ComponentTypeName)
            .HasMaxLength(FieldLengths.ComponentTypeName);

        builder.Property(b => b.IconKey)
            .HasMaxLength(FieldLengths.IconKey);

        builder.Property(b => b.SummaryTemplate)
            .HasMaxLength(FieldLengths.SummaryTemplate);

        // Block type keys are global, not scoped to a template: the same "quote" block is reused
        // across templates, and a payload names it by key alone.
        builder.HasIndex(b => b.Key)
            .IsUnique();

        builder.HasMany(b => b.Properties)
            .WithOne(p => p.BlockType)
            .HasForeignKey(p => p.BlockTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Revisions)
            .WithOne(r => r.BlockType)
            .HasForeignKey(r => r.BlockTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(CmsSeedData.RawHtmlBlockType);
    }
}
