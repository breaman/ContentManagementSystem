using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class SearchDocumentConfiguration : IEntityTypeConfiguration<SearchDocument>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SearchDocument> builder)
    {
        builder.Property(d => d.EntityType)
            .HasConversion<int>();

        builder.Property(d => d.Title)
            .HasMaxLength(FieldLengths.ContentTitle)
            .IsRequired();

        builder.Property(d => d.Body)
            .HasColumnType(ColumnTypes.UnboundedText);

        builder.Property(d => d.Keywords)
            .HasColumnType(ColumnTypes.UnboundedText);

        builder.Property(d => d.Url)
            .HasMaxLength(FieldLengths.Url);

        builder.Property(d => d.UpdatedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        // One projection per thing, which is what makes the indexer an upsert. Without it a page
        // saved twice is two search results for one page.
        builder.HasIndex(d => new { d.EntityType, d.EntityId })
            .IsUnique();

        // The full-text index itself is created by raw SQL in the migration: EF Core models no
        // full-text catalog, and the statements differ between Azure SQL and a self-hosted
        // instance.
    }
}
