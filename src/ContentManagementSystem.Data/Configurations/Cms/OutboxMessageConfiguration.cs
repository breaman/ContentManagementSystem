using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type)
            .HasMaxLength(FieldLengths.EventType)
            .IsRequired();

        builder.Property(m => m.PayloadJson)
            .HasColumnType(ColumnTypes.Json)
            .IsRequired();

        builder.Property(m => m.CreatedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(m => m.ProcessedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(m => m.LastError)
            .HasMaxLength(FieldLengths.Reason);

        // The processor's only question, asked every five seconds by every instance: what is still
        // pending, oldest first? Filtered so the index holds the pending rows alone — the processed
        // history is the part of this table that grows, and it must not be paged in to answer that.
        builder.HasIndex(m => m.CreatedOn)
            .HasFilter($"[{nameof(OutboxMessage.ProcessedOn)}] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Pending");
    }
}
