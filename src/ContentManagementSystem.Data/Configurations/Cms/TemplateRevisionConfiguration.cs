using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class TemplateRevisionConfiguration : IEntityTypeConfiguration<TemplateRevision>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TemplateRevision> builder)
    {
        builder.Property(r => r.ZoneSnapshotJson)
            .HasColumnType(ColumnTypes.Json)
            .IsRequired();

        builder.Property(r => r.Notes)
            .HasMaxLength(FieldLengths.RevisionNotes);

        // Revision numbers are allocated per template, and a page version pins itself to one of
        // them. A duplicate would make that pin resolve to two different schemas.
        builder.HasIndex(r => new { r.TemplateId, r.RevisionNumber })
            .IsUnique();
    }
}
