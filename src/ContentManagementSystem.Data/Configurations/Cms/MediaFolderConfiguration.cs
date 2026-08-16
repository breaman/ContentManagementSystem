using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class MediaFolderConfiguration : IEntityTypeConfiguration<MediaFolder>
{
    /// <summary>Name of the filtered index serving the folder tree, asserted by the schema tests.</summary>
    public const string LiveChildrenIndexName = "IX_MediaFolders_ParentId_SortOrder_Live";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaFolder> builder)
    {
        builder.Property(folder => folder.Name)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(folder => folder.Path)
            .HasMaxLength(FieldLengths.MaterializedPath)
            .IsRequired();

        // The browser's own query: the live children of a folder, in display order.
        builder.HasIndex(folder => new { folder.ParentId, folder.SortOrder })
            .HasDatabaseName(LiveChildrenIndexName)
            .HasFilter($"[{nameof(MediaFolder.IsDeleted)}] = 0");

        // Descendant lookups are a prefix match on the materialized path. Unfiltered, because
        // restoring a deleted folder has to find the deleted rows beneath it.
        builder.HasIndex(folder => folder.Path);

        builder.HasOne(folder => folder.Parent)
            .WithMany(folder => folder.Children)
            .HasForeignKey(folder => folder.ParentId)
            // Deleting a parent out from under its children would orphan a whole subtree, and
            // folders are retired by flag anyway.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(folder => !folder.IsDeleted);
    }
}
