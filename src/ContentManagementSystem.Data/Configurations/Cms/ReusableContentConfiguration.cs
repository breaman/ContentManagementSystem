using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class ReusableContentConfiguration : IEntityTypeConfiguration<ReusableContent>
{
    /// <summary>Name of the filtered index serving the library list, asserted by the schema tests.</summary>
    public const string LiveLibraryIndexName = "IX_ReusableContents_FolderId_Name_Live";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReusableContent> builder)
    {
        builder.Property(item => item.Key)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(item => item.Name)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(item => item.Description)
            .HasMaxLength(FieldLengths.ShortDescription);

        builder.Property(item => item.RowVersion)
            .IsRowVersion();

        // Unfiltered on purpose, unlike the library index below: a key is the identity a payload
        // import or a deployment script quotes, and a deleted item holding one still owns it. A
        // filtered unique index would let a second item take the key of one sitting in the recycle
        // bin, and restoring the first would then fail on a constraint nobody could see coming.
        builder.HasIndex(item => item.Key)
            .IsUnique();

        // The library screen's own query: the live items of a folder, in display order.
        builder.HasIndex(item => new { item.FolderId, item.Name })
            .HasDatabaseName(LiveLibraryIndexName)
            .HasFilter($"[{nameof(ReusableContent.IsDeleted)}] = 0");

        // "Which items are shaped by this block type" — asked before a block type may be retired,
        // and by the picker when an editor creates an item of a given shape.
        builder.HasIndex(item => item.BlockTypeId);

        builder.HasOne(item => item.BlockType)
            .WithMany()
            .HasForeignKey(item => item.BlockTypeId)
            // The same backstop PageConfiguration puts behind Template: the service layer refuses to
            // delete a block type while a live item is shaped by it, and an item whose block type
            // row is gone has no schema to validate or render against.
            .OnDelete(DeleteBehavior.Restrict);

        // The item and its versions reference each other, so neither insert can carry the other's
        // key. Both directions are Restrict and the service sets DraftVersionId in a second
        // statement inside the creating transaction (spec section 23.5). Cascade here would also be
        // a cycle SQL Server refuses outright.
        builder.HasOne(item => item.DraftVersion)
            .WithMany()
            .HasForeignKey(item => item.DraftVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.PublishedVersion)
            .WithMany()
            .HasForeignKey(item => item.PublishedVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Soft-deleted items are invisible to every ordinary query, including the resolver on the
        // delivery path — which is what makes "deleting a reusable item stops it rendering" true
        // without the resolver having to remember to ask.
        builder.HasQueryFilter(item => !item.IsDeleted);
    }
}
