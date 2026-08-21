using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class SiteStylesheetRevisionConfiguration : IEntityTypeConfiguration<SiteStylesheetRevision>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SiteStylesheetRevision> builder)
    {
        builder.Property(revision => revision.Css)
            .HasColumnType(ColumnTypes.UnboundedText)
            .IsRequired();

        builder.Property(revision => revision.Hash)
            .HasColumnType(ColumnTypes.Sha256Hash)
            .IsRequired();

        builder.Property(revision => revision.Note)
            .HasMaxLength(FieldLengths.RevisionNotes);

        builder.Property(revision => revision.CreatedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.HasOne(revision => revision.SiteStylesheet)
            .WithMany(sheet => sheet.Revisions)
            .HasForeignKey(revision => revision.SiteStylesheetId)
            .OnDelete(DeleteBehavior.Restrict);

        // The revision list is the only query over this table and it is always newest first.
        builder.HasIndex(revision => revision.CreatedOn)
            .IsDescending();
    }
}
