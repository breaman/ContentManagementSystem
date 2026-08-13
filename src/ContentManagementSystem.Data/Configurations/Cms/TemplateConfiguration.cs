using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.Property(t => t.Key)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(t => t.Name)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(FieldLengths.ShortDescription);

        builder.Property(t => t.ComponentTypeName)
            .HasMaxLength(FieldLengths.ComponentTypeName);

        // The key is quoted in every payload authored against this template, so a duplicate would
        // make a payload ambiguous about which template it belongs to.
        builder.HasIndex(t => t.Key)
            .IsUnique();

        builder.HasMany(t => t.Zones)
            .WithOne(z => z.Template)
            .HasForeignKey(z => z.TemplateId)
            // Deleting a template that still has pages is blocked in the service layer; cascading
            // here would silently take the zone definitions with it if that guard were ever
            // bypassed, leaving payloads with no schema to validate against.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Revisions)
            .WithOne(r => r.Template)
            .HasForeignKey(r => r.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
