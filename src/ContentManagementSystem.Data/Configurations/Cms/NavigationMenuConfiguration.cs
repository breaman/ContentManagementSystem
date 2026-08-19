using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class NavigationMenuConfiguration : IEntityTypeConfiguration<NavigationMenu>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NavigationMenu> builder)
    {
        builder.Property(m => m.Key)
            .HasMaxLength(FieldLengths.ContentKey)
            .IsRequired();

        builder.Property(m => m.Name)
            .HasMaxLength(FieldLengths.EntityName)
            .IsRequired();

        builder.Property(m => m.Description)
            .HasMaxLength(FieldLengths.ShortDescription);

        // The key is the address: a template asks for a menu by it and the cache tag is built from
        // it, so two menus sharing one would make both unaddressable.
        builder.HasIndex(m => m.Key)
            .IsUnique();
    }
}
