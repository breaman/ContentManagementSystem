using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class SiteSettingsConfiguration : IEntityTypeConfiguration<SiteSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SiteSettings> builder)
    {
        // The row is seeded with an explicit id and never inserted again, so identity generation
        // would only offer a second way to create one.
        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.SiteName)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(s => s.Culture)
            .HasMaxLength(FieldLengths.Culture)
            .HasDefaultValue("en-US")
            .IsRequired();

        builder.Property(s => s.TimeZoneId)
            .HasMaxLength(FieldLengths.TimeZoneId)
            .IsRequired();

        builder.Property(s => s.RobotsTxt)
            .HasColumnType(ColumnTypes.UnboundedText);

        // Stored as the underlying int. The names are a UI concern; the numbers are the contract.
        builder.Property(s => s.WorkflowMode)
            .HasConversion<int>();

        builder.Property(s => s.GoogleSiteVerification)
            .HasMaxLength(FieldLengths.VerificationToken);

        // Deferred from P1-01 until Page existed. Navigation-less because nothing traverses from a
        // page back to the settings row, and Restrict because deleting the page the site root
        // resolves to is a decision somebody has to make deliberately, not a side effect.
        builder.HasOne<Page>()
            .WithMany()
            .HasForeignKey(s => s.HomePageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Page>()
            .WithMany()
            .HasForeignKey(s => s.NotFoundPageId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enforced in the database rather than by convention: a second settings row would make
        // "the site's culture" a question with two answers, and nothing in the code reads more
        // than the first row it finds.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_SiteSettings_SingleRow",
            $"[{nameof(SiteSettings.Id)}] = {SiteSettings.SingletonId}"));

        builder.HasData(CmsSeedData.SiteSettings);
    }
}
