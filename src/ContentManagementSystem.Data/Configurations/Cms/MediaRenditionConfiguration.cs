using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class MediaRenditionConfiguration : IEntityTypeConfiguration<MediaRendition>
{
    /// <summary>Name of the lookup index the delivery endpoint hits, asserted by the schema tests.</summary>
    public const string SpecIndexName = "IX_MediaRenditions_MediaItemId_SpecHash";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaRendition> builder)
    {
        builder.Property(rendition => rendition.SpecHash)
            .HasColumnType(ColumnTypes.Sha256Hash)
            .IsRequired();

        builder.Property(rendition => rendition.Spec)
            .HasMaxLength(FieldLengths.RenditionSpec)
            .IsRequired();

        builder.Property(rendition => rendition.Format)
            .HasMaxLength(FieldLengths.ImageFormat)
            .IsRequired();

        builder.Property(rendition => rendition.StorageKey)
            .HasMaxLength(FieldLengths.StorageKey)
            .IsRequired();

        // The delivery endpoint's only query, and the constraint that keeps the per-key semaphore
        // honest: if two requests raced past it, the second insert fails rather than producing a
        // duplicate row pointing at a second copy of identical bytes.
        builder.HasIndex(rendition => new { rendition.MediaItemId, rendition.SpecHash })
            .IsUnique()
            .HasDatabaseName(SpecIndexName);

        builder.HasOne(rendition => rendition.MediaItem)
            .WithMany(item => item.Renditions)
            .HasForeignKey(rendition => rendition.MediaItemId)
            // Cascade, unlike everywhere else in this schema. Renditions are derived data with no
            // independent meaning: an item that is permanently deleted — which only happens once
            // nothing references it — leaves nothing here worth keeping.
            .OnDelete(DeleteBehavior.Cascade);
    }
}
