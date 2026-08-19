using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class NavigationItemConfiguration : IEntityTypeConfiguration<NavigationItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NavigationItem> builder)
    {
        builder.Property(i => i.Label)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(i => i.ExternalUrl)
            .HasMaxLength(FieldLengths.Url);

        builder.HasOne(i => i.Menu)
            .WithMany(m => m.Items)
            .HasForeignKey(i => i.NavigationMenuId)
            // The items are the menu. Deleting one and leaving its entries orphaned would leave
            // rows nothing can reach and nothing can render.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Parent)
            .WithMany(i => i.Children)
            .HasForeignKey(i => i.ParentId)
            // Restrict on the self-reference: SQL Server refuses multiple cascade paths into one
            // table, and deleting a heading out from under its children is a decision for the menu
            // editor rather than for the database.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Page)
            .WithMany()
            .HasForeignKey(i => i.PageId)
            // Restrict, because a page is soft-deleted rather than removed: a menu item pointing at
            // a recycled page renders as nothing and comes back when the page is restored, which is
            // what an editor emptying the bin expects to have to think about.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.NavigationMenuId, i.ParentId, i.SortOrder })
            .HasDatabaseName("IX_NavigationItems_Menu_Parent_SortOrder");

        // Exactly one target. A row with both would have two answers to "where does this go", and a
        // row with neither renders a link to nowhere — neither is repairable from the rendered page.
        //
        // Written as a count rather than the obvious `(x IS NULL) <> (y IS NULL)`: in T-SQL a
        // predicate is not a value, so comparing two of them is a syntax error rather than the
        // exclusive-or it reads as.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NavigationItems_OneTarget",
            $"""
             (CASE WHEN [{nameof(NavigationItem.PageId)}] IS NULL THEN 0 ELSE 1 END +
              CASE WHEN [{nameof(NavigationItem.ExternalUrl)}] IS NULL THEN 0 ELSE 1 END) = 1
             """));
    }
}
