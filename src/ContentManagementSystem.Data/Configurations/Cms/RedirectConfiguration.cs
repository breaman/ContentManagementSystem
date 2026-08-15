using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class RedirectConfiguration : IEntityTypeConfiguration<Redirect>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Redirect> builder)
    {
        builder.Property(r => r.FromUrl)
            .HasMaxLength(FieldLengths.Url)
            .IsRequired();

        builder.Property(r => r.FromUrlHash)
            .HasColumnType(ColumnTypes.Sha256Hash)
            .IsRequired();

        builder.Property(r => r.ToUrl)
            .HasMaxLength(FieldLengths.Url);

        builder.Property(r => r.Notes)
            .HasMaxLength(FieldLengths.ShortDescription);

        builder.Property(r => r.LastHitOn)
            .HasColumnType(ColumnTypes.Timestamp);

        // One rule per source URL, unconditionally: two rows telling the same URL to go to two
        // places is not a state the resolver could pick between. Unfiltered, unlike the route index,
        // because a disabled redirect still occupies its FromUrl — re-enabling it must not be a
        // constraint violation waiting to happen.
        builder.HasIndex(r => r.FromUrlHash)
            .IsUnique();

        // Chain flattening asks "what points at this destination" whenever a redirect is written,
        // and the where-used report asks it of a page before deleting it.
        builder.HasIndex(r => r.ToPageId);

        builder.HasOne(r => r.ToPage)
            .WithMany()
            .HasForeignKey(r => r.ToPageId)
            // A redirect pointing at a page that is being permanently deleted must not block the
            // delete, but it must also not become a redirect to nothing. RedirectService rewrites
            // these to a literal URL before the purge; Restrict is the backstop that makes a missed
            // rewrite a loud failure rather than an orphan.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
