using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class PageTagConfiguration : IEntityTypeConfiguration<PageTag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PageTag> builder)
    {
        builder.HasOne(t => t.Page)
            .WithMany()
            .HasForeignKey(t => t.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Tag)
            .WithMany(t => t.Pages)
            .HasForeignKey(t => t.TagId)
            // Restrict: deleting a tag that is in use is a decision with a page count attached to
            // it, and the tag admin screen asks. A cascade would make it invisible.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.PageId, t.TagId })
            .IsUnique();

        // The filter's own direction: every page carrying this tag.
        builder.HasIndex(t => t.TagId);
    }
}
