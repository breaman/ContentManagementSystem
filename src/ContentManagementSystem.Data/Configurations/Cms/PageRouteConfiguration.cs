using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class PageRouteConfiguration : IEntityTypeConfiguration<PageRoute>
{
    /// <summary>Name of the filtered unique index on published URLs, asserted by the schema tests.</summary>
    public const string PublishedUrlIndexName = "IX_PageRoutes_UrlHash_Published";

    /// <summary>Name of the unfiltered lookup index preview resolves through.</summary>
    public const string UrlIndexName = "IX_PageRoutes_UrlHash";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PageRoute> builder)
    {
        builder.Property(r => r.Url)
            .HasMaxLength(FieldLengths.Url)
            .IsRequired();

        builder.Property(r => r.UrlHash)
            .HasColumnType(ColumnTypes.Sha256Hash)
            .IsRequired();

        builder.Property(r => r.CreatedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        // Two published pages may not claim the same URL. Filtered, so the draft route of a page
        // being prepared to replace a live one can sit at the same address without violating it —
        // which is the ordinary case, not an edge case (spec sections 10.4 and 23.5).
        builder.HasIndex(r => r.UrlHash)
            .IsUnique()
            .HasDatabaseName(PublishedUrlIndexName)
            .HasFilter($"[{nameof(PageRoute.IsPublished)}] = 1");

        // Preview resolves a draft-only route, which the filtered index above cannot serve. Named
        // explicitly because EF Core hands back the same index builder for a repeated property set:
        // a second unnamed HasIndex on UrlHash silently reconfigures the filtered one instead of
        // adding anything, and the miss is invisible until somebody reads a query plan.
        builder.HasIndex(r => r.UrlHash, UrlIndexName);

        // "What are this page's routes" — asked by every move, every publish, and the URL shown in
        // the editor.
        builder.HasIndex(r => r.PageId);

        builder.HasOne(r => r.Page)
            .WithMany()
            .HasForeignKey(r => r.PageId)
            // Routes are derived data with no life of their own: a page that is permanently deleted
            // has no URLs, and Restrict here would make the recycle bin's purge fail on rows it
            // would only have had to delete itself.
            .OnDelete(DeleteBehavior.Cascade);

        // Deliberately no query filter on the page's IsDeleted. A soft-deleted page's routes are
        // removed by RecycleBinService rather than hidden, because a route that is invisible to the
        // application but present in the unique index is a URL nobody can reuse and nobody can find.
    }
}
