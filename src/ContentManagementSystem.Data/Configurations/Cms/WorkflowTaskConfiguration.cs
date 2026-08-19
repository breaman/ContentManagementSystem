using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContentManagementSystem.Data.Configurations.Cms;

/// <inheritdoc />
public class WorkflowTaskConfiguration : IEntityTypeConfiguration<WorkflowTask>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkflowTask> builder)
    {
        builder.Property(t => t.State)
            .HasConversion<int>();

        builder.Property(t => t.DueDate)
            .HasColumnType(ColumnTypes.BusinessDate);

        builder.Property(t => t.DecidedOn)
            .HasColumnType(ColumnTypes.Timestamp);

        builder.Property(t => t.SubmissionNote)
            .HasMaxLength(FieldLengths.WorkflowNote);

        builder.Property(t => t.DecisionNote)
            .HasMaxLength(FieldLengths.WorkflowNote);

        // At most one open request per version. Filtered so that the settled rounds — which are the
        // history of a page that has been round the loop three times — can sit beside it.
        builder.HasIndex(t => t.PageVersionId)
            .IsUnique()
            .HasFilter($"[{nameof(WorkflowTask.State)}] = {(int)WorkflowState.Pending}")
            .HasDatabaseName("UX_WorkflowTasks_PageVersionId_Pending");

        // "What is waiting on me" — the inbox query, which filters on assignee and state together.
        builder.HasIndex(t => new { t.AssignedToUserId, t.State });

        // "What is happening with this page", for the review panel and the dashboard tile.
        builder.HasIndex(t => new { t.PageId, t.State });

        builder.HasOne(t => t.Page)
            .WithMany()
            .HasForeignKey(t => t.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.PageVersion)
            .WithMany()
            .HasForeignKey(t => t.PageVersionId)
            // Restrict rather than cascade: retention prunes versions, and a request for review has
            // to keep naming the exact version it was about. The pruner settles open tasks first.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.AssignedTo)
            .WithMany()
            .HasForeignKey(t => t.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.DecidedBy)
            .WithMany()
            .HasForeignKey(t => t.DecidedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
