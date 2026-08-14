using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class PageVersionConfiguration : IEntityTypeConfiguration<PageVersion>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PageVersion> builder)
    {
        builder.Property(v => v.Label)
            .HasMaxLength(FieldLengths.EntityName);

        builder.Property(v => v.Title)
            .HasMaxLength(FieldLengths.ContentTitle)
            .IsRequired();

        builder.Property(v => v.ContentJson)
            .HasColumnType(ColumnTypes.Json)
            .IsRequired();

        builder.Property(v => v.MetaTitle)
            .HasMaxLength(FieldLengths.EntityName);

        builder.Property(v => v.MetaDescription)
            .HasMaxLength(FieldLengths.MetaDescription);

        builder.Property(v => v.CanonicalUrl)
            .HasMaxLength(FieldLengths.Url);

        builder.Property(v => v.OgTitle)
            .HasMaxLength(FieldLengths.EntityName);

        builder.Property(v => v.OgDescription)
            .HasMaxLength(FieldLengths.MetaDescription);

        builder.Property(v => v.OgType)
            .HasMaxLength(FieldLengths.SocialCardType);

        builder.Property(v => v.TwitterCard)
            .HasMaxLength(FieldLengths.SocialCardType);

        builder.Property(v => v.StructuredDataJson)
            .HasColumnType(ColumnTypes.Json);

        builder.Property(v => v.ChangeFreq)
            .HasMaxLength(FieldLengths.ChangeFrequency);

        // Overrides the model-wide decimal convention: sitemap priority is one decimal place, and
        // decimal(18,2) would store a 0.55 no search engine reads back as written.
        builder.Property(v => v.Priority)
            .HasColumnType(ColumnTypes.SitemapPriority);

        // Stored as the underlying tinyint. The names are a UI concern; the numbers are the
        // contract, and they appear in two of the indexes below.
        builder.Property(v => v.Status)
            .HasConversion<byte>();

        builder.Property(v => v.RowVersion)
            .IsRowVersion();

        // A version number identifies a version to an editor, so two rows sharing one within a page
        // would make "restore version 4" ambiguous. Unique rather than merely indexed because the
        // number is computed from the current maximum, and two concurrent publishes race for it —
        // this is what turns that race into a failed save instead of a duplicate.
        builder.HasIndex(v => new { v.PageId, v.VersionNumber })
            .IsUnique();

        // Version history and "find this page's draft" both filter by status within a page.
        builder.HasIndex(v => new { v.PageId, v.Status });

        // The scheduler's query: everything approved and due (spec section 11.6).
        builder.HasIndex(v => new { v.Status, v.PublishOn });

        builder.HasOne(v => v.Page)
            .WithMany(p => p.Versions)
            .HasForeignKey(v => v.PageId)
            // The other half of the mutual reference described on PageConfiguration. Version
            // history is the thing a soft delete exists to preserve, so cascading it away on a hard
            // delete of the page would defeat the point.
            .OnDelete(DeleteBehavior.Restrict);

        // Navigation-less on purpose: a version's template is a captured coordinate rather than a
        // relationship anyone traverses, and a collection of every version on Template would be a
        // navigation nothing wants to load. The constraint still earns its place — a version whose
        // template row has been deleted can neither be rendered nor diffed.
        builder.HasOne<Template>()
            .WithMany()
            .HasForeignKey(v => v.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
