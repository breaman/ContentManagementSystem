using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ScheduledJob> builder)
    {
        builder.Property(j => j.Kind)
            .HasConversion<int>();

        builder.Property(j => j.State)
            .HasConversion<int>();

        builder.Property(j => j.RunOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(j => j.ClaimedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(j => j.CompletedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(j => j.ClaimedBy)
            .HasMaxLength(FieldLengths.SchedulerInstance);

        builder.Property(j => j.FailureReason)
            .HasMaxLength(FieldLengths.Reason);

        // The poll runs every 30 seconds on every instance and asks exactly one question: which
        // pending jobs are due? Leading with State keeps the scan off the completed history, which
        // is the part of this table that grows.
        builder.HasIndex(j => new { j.State, j.RunOn })
            .HasDatabaseName("IX_ScheduledJobs_State_RunOn");

        // At most one outstanding job of a kind per page, so rescheduling replaces rather than
        // stacks — two pending publishes for one page is the shape of a double publish.
        builder.HasIndex(j => new { j.PageId, j.Kind })
            .IsUnique()
            .HasFilter($"[{nameof(ScheduledJob.State)}] IN ({(int)ScheduledJobState.Pending}, {(int)ScheduledJobState.Claimed})")
            .HasDatabaseName("UX_ScheduledJobs_PageId_Kind_Outstanding");

        builder.HasOne(j => j.Page)
            .WithMany()
            .HasForeignKey(j => j.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(j => j.PageVersion)
            .WithMany()
            .HasForeignKey(j => j.PageVersionId)
            // Restrict: a job that lost the version it was going to publish would run and publish
            // nothing. Cancelling the job is the pruner's business, not the database's.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(j => j.Owner)
            .WithMany()
            .HasForeignKey(j => j.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
