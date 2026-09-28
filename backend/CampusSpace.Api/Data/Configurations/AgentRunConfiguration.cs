using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    /// <summary>At most one live (non-terminal) run per request. GlobalExceptionHandler maps a violation to a 409.</summary>
    public const string LiveRunIndex = "IX_AgentRuns_RequestId_Live";
    public const string LiveRunMessage = "Request already has a live agent run";
    public const int ModelMaxLength = 100;

    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        var statuses = SqlList(AgentRunStatuses.All);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AgentRuns_RevisionNo", "\"RevisionNo\" > 0");
            t.HasCheckConstraint("CK_AgentRuns_Status", $"\"Status\" IN ({statuses})");
            t.HasCheckConstraint("CK_AgentRuns_DurationMs", "\"DurationMs\" IS NULL OR \"DurationMs\" >= 0");
            t.HasCheckConstraint("CK_AgentRuns_CompletedAt_After_StartedAt",
                "\"StartedAt\" IS NULL OR \"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"StartedAt\"");
            t.HasCheckConstraint("CK_AgentRuns_FailureReason",
                $"\"Status\" <> '{AgentRunStatuses.Failed}' OR \"FailureReason\" IS NOT NULL");
        });

        builder.HasKey(r => r.Id);
        // .NET generates the id (it is the LangGraph thread_id); the database never does.
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).IsRequired().HasMaxLength(20);
        builder.Property(r => r.PlanJson).HasColumnType("jsonb");
        builder.Property(r => r.ProposalJson).HasColumnType("jsonb");
        builder.Property(r => r.PolicySnapshotJson).HasColumnType("jsonb");
        builder.Property(r => r.Model).HasMaxLength(ModelMaxLength);
        builder.Property(r => r.Nodes).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");

        builder.HasOne(r => r.Request).WithMany().HasForeignKey(r => r.RequestId).OnDelete(DeleteBehavior.Restrict);

        // Also serves as the RequestId FK index.
        builder.HasIndex(r => new { r.RequestId, r.RevisionNo }).IsUnique();
        builder.HasIndex(r => r.RequestId, LiveRunIndex).IsUnique()
            .HasFilter($"\"Status\" IN ({SqlList(AgentRunStatuses.Active)})");
        // The poller's queue: live runs by status, oldest first.
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
    }

    internal static string SqlList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
