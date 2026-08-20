using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class PageConfiguration : IEntityTypeConfiguration<Page>
{
    /// <summary>Name of the filtered index serving the tree, asserted by the schema tests.</summary>
    public const string LiveChildrenIndexName = "IX_Pages_ParentId_SortOrder_Live";

    /// <summary>Name of the index the structural menu is built from.</summary>
    public const string NavigationIndexName = "IX_Pages_Navigation";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.Property(p => p.Slug)
            .HasMaxLength(FieldLengths.Slug)
            .IsRequired();

        builder.Property(p => p.ExplicitUrl)
            .HasMaxLength(FieldLengths.Url);

        builder.Property(p => p.Path)
            .HasMaxLength(FieldLengths.MaterializedPath)
            .IsRequired();

        builder.Property(p => p.InternalNotes)
            .HasMaxLength(FieldLengths.InternalNotes);

        builder.Property(p => p.ReviewByDate)
            .HasColumnType(ColumnTypes.BusinessDate);

        // Optimistic concurrency is the authoritative layer; edit locks are only cooperative UX
        // (spec section 11.8). Without this, two editors moving the same page in the tree produce a
        // last-write-wins outcome that neither of them is told about.
        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        // Addressable from outside the database without exposing a sequential id.
        builder.HasIndex(p => p.PublicId)
            .IsUnique();

        // The tree's own query: the live children of a node, in order. Filtered because the recycle
        // bin is the only caller that wants the deleted ones, and it asks for them explicitly.
        builder.HasIndex(p => new { p.ParentId, p.SortOrder })
            .HasDatabaseName(LiveChildrenIndexName)
            .HasFilter($"[{nameof(Page.IsDeleted)}] = 0");

        // Descendant lookups are a prefix match against the materialized path. Deliberately
        // unfiltered: restoring a deleted subtree has to find the deleted rows.
        builder.HasIndex(p => p.Path);

        // Delivery resolves a route to a page and then loads exactly this version.
        builder.HasIndex(p => p.PublishedVersionId);

        // The structural menu, which every public render builds: the top levels of the tree, in
        // order, for pages that are live and asked to appear. Without this it is a scan of every
        // page in the site on every uncached render — 7.8 ms of a 20 ms render at fifty thousand
        // pages, and growing with the site (task P9-14). The filter is what keeps it small: it
        // indexes only the rows a menu can contain, and the ordering columns are the key, so the
        // sort comes for free. ParentId and PublishedVersionId are included rather than keyed
        // because the query returns them without ever searching on them.
        builder.HasIndex(p => new { p.Depth, p.SortOrder })
            .HasDatabaseName(NavigationIndexName)
            .IncludeProperties(p => new { p.ParentId, p.PublishedVersionId })
            .HasFilter($"[{nameof(Page.IsDeleted)}] = 0 AND [{nameof(Page.ShowInNavigation)}] = 1");

        builder.HasOne(p => p.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(p => p.ParentId)
            // Deleting a parent out from under its children would orphan a whole subtree, and
            // pages are retired by flag anyway — RecycleBinService walks the subtree itself.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Template)
            .WithMany()
            .HasForeignKey(p => p.TemplateId)
            // The service layer refuses to delete a template while a live page uses it; this is the
            // backstop for the case where that guard is bypassed, since a page whose template row is
            // gone has no schema to render or validate against.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Owner)
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            // A departing employee's account may be removed; their pages must not go with it.
            .OnDelete(DeleteBehavior.SetNull);

        // Page and PageVersion reference each other, so neither insert can carry the other's key.
        // Both directions are Restrict and PageService sets DraftVersionId in a second statement
        // inside the creating transaction (spec section 23.5). Cascade here would also be a cycle
        // SQL Server refuses outright.
        builder.HasOne(p => p.DraftVersion)
            .WithMany()
            .HasForeignKey(p => p.DraftVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PublishedVersion)
            .WithMany()
            .HasForeignKey(p => p.PublishedVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Soft-deleted pages are invisible to every ordinary query. The recycle bin, and anything
        // else that means it, calls IgnoreQueryFilters (spec section 23.5).
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
