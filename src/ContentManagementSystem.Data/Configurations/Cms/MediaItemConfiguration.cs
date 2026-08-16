using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    /// <summary>Name of the deduplication index, asserted by the schema tests.</summary>
    public const string LiveHashIndexName = "IX_MediaItems_Sha256_Live";

    /// <summary>Name of the index serving the library's kind filter, asserted by the schema tests.</summary>
    public const string KindIndexName = "IX_MediaItems_MediaKind_IsDeleted";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.Property(item => item.FileName)
            .HasMaxLength(FieldLengths.FileName)
            .IsRequired();

        builder.Property(item => item.OriginalFileName)
            .HasMaxLength(FieldLengths.FileName)
            .IsRequired();

        builder.Property(item => item.ContentType)
            .HasMaxLength(FieldLengths.MimeType)
            .IsRequired();

        builder.Property(item => item.Sha256)
            .HasColumnType(ColumnTypes.Sha256Hash)
            .IsRequired();

        builder.Property(item => item.StorageKey)
            .HasMaxLength(FieldLengths.StorageKey)
            .IsRequired();

        builder.Property(item => item.MediaKind)
            .HasConversion<byte>();

        builder.Property(item => item.AltText)
            .HasMaxLength(FieldLengths.ShortDescription);

        builder.Property(item => item.Title)
            .HasMaxLength(FieldLengths.EntityName);

        builder.Property(item => item.Caption)
            .HasMaxLength(FieldLengths.Caption);

        builder.Property(item => item.Credit)
            .HasMaxLength(FieldLengths.EntityName);

        builder.Property(item => item.EditsJson)
            .HasColumnType(ColumnTypes.Json);

        builder.Property(item => item.RowVersion)
            .IsRowVersion();

        // Deduplication, as a constraint rather than a check the upload pipeline could skip:
        // identical bytes cannot produce a second live row. Filtered on IsDeleted so that an item
        // sitting in the recycle bin does not permanently block re-uploading the same file — the
        // trap a plain unique index would set here (spec section 23.3).
        builder.HasIndex(item => item.Sha256)
            .IsUnique()
            .HasDatabaseName(LiveHashIndexName)
            .HasFilter($"[{nameof(MediaItem.IsDeleted)}] = 0");

        // The browser's folder listing.
        builder.HasIndex(item => item.FolderId);

        // "Images only", which is what every picker asks for.
        builder.HasIndex(item => new { item.MediaKind, item.IsDeleted })
            .HasDatabaseName(KindIndexName);

        builder.HasOne(item => item.Folder)
            .WithMany(folder => folder.Items)
            .HasForeignKey(item => item.FolderId)
            // Deleting a folder must not take the files in it with it. The service moves items to
            // the parent folder first; this is the backstop for anything that skips that.
            .OnDelete(DeleteBehavior.Restrict);

        // Soft-deleted items are invisible to every ordinary query, including the picker and the
        // delivery endpoint — which is what makes "deleting an image stops it being served" true
        // without either of them having to remember to ask (spec section 23.5).
        builder.HasQueryFilter(item => !item.IsDeleted);
    }
}
