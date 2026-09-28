using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public const int CommentMaxLength = 1000;

    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ApprovalDecisions_Decision",
                $"\"Decision\" IN ({AgentRunConfiguration.SqlList(ApprovalDecisions.All)})");
            // Reject and Revise must say why (the officer screens require it too).
            t.HasCheckConstraint("CK_ApprovalDecisions_Comment",
                $"\"Decision\" = '{ApprovalDecisions.Approve}' OR (\"Comment\" IS NOT NULL AND btrim(\"Comment\") <> '')");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Decision).IsRequired().HasMaxLength(10);
        builder.Property(d => d.Comment).HasMaxLength(CommentMaxLength);
        builder.Property(d => d.DecidedAt).IsRequired();

        builder.HasOne(d => d.Request).WithMany().HasForeignKey(d => d.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.AgentRun).WithMany().HasForeignKey(d => d.AgentRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Officer).WithMany().HasForeignKey(d => d.OfficerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.RequestId);
        builder.HasIndex(d => d.AgentRunId);
        builder.HasIndex(d => d.OfficerId);
    }
}
