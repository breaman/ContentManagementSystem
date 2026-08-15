using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class PreviewTokenConfiguration : IEntityTypeConfiguration<PreviewToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PreviewToken> builder)
    {
        builder.Property(t => t.TokenHash)
            .HasColumnType(ColumnTypes.Sha256Hash)
            .IsRequired();

        builder.Property(t => t.ExpiresOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(t => t.RevokedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(t => t.Notes)
            .HasMaxLength(FieldLengths.ShortDescription);

        // The lookup a shared link performs, and the guarantee that two tokens cannot collide.
        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        // The revocation UI lists a page's tokens; bulk revocation on unpublish walks the same set.
        builder.HasIndex(t => t.PageId);

        builder.HasOne(t => t.Page)
            .WithMany()
            .HasForeignKey(t => t.PageId)
            // A permanently deleted page has nothing to preview, and a token surviving it would be
            // a link to a version the purge already removed.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.PageVersion)
            .WithMany()
            .HasForeignKey(t => t.PageVersionId)
            // Restrict, unlike the page: version retention prunes old versions, and a token pointing
            // at one must stop that prune rather than silently lose what it was issued for. The
            // pruner revokes tokens first.
            .OnDelete(DeleteBehavior.Restrict);

        // No query filter on the page's IsDeleted. Token validation must be able to see a token
        // whose page has been recycled, so it can answer "this preview is no longer available"
        // rather than "this link is invalid" — the two send a reviewer to different people.
    }
}
