using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class SiteStylesheetConfiguration : IEntityTypeConfiguration<SiteStylesheet>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SiteStylesheet> builder)
    {
        // Seeded with an explicit id and never inserted again, like SiteSettings beside it.
        builder.Property(sheet => sheet.Id)
            .ValueGeneratedNever();

        builder.Property(sheet => sheet.DraftCss)
            .HasColumnType(ColumnTypes.UnboundedText)
            .IsRequired();

        builder.Property(sheet => sheet.PublishedCss)
            .HasColumnType(ColumnTypes.UnboundedText);

        builder.Property(sheet => sheet.PublishedHash)
            .HasColumnType(ColumnTypes.Sha256Hash);

        builder.Property(sheet => sheet.PublishedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(sheet => sheet.RowVersion)
            .IsRowVersion();

        // The published copy points at the revision it was cut from, and the revisions point back
        // at the stylesheet. Both directions are Restrict, so the pair of foreign keys is a cycle
        // SQL Server accepts — what it refuses is a cycle of cascades.
        builder.HasOne(sheet => sheet.PublishedRevision)
            .WithMany()
            .HasForeignKey(sheet => sheet.PublishedRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enforced in the database rather than by convention: a second row would make "what CSS is
        // the site serving" a question with two answers, and every reader takes the first row it
        // finds.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_SiteStylesheet_SingleRow",
            $"[{nameof(SiteStylesheet.Id)}] = {SiteStylesheet.SingletonId}"));

        builder.HasData(CmsSeedData.SiteStylesheet);
    }
}
