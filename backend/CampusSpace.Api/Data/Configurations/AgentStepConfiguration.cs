using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class AgentStepConfiguration : IEntityTypeConfiguration<AgentStep>
{
    public const int AgentNameMaxLength = 50;

    public void Configure(EntityTypeBuilder<AgentStep> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AgentSteps_Sequence", "\"Sequence\" >= 1");
            t.HasCheckConstraint("CK_AgentSteps_AgentName_NotBlank", "btrim(\"AgentName\") <> ''");
            t.HasCheckConstraint("CK_AgentSteps_Status", $"\"Status\" IN ({AgentRunConfiguration.SqlList(AgentStepStatuses.All)})");
            t.HasCheckConstraint("CK_AgentSteps_Retries", "\"Retries\" >= 0");
            t.HasCheckConstraint("CK_AgentSteps_DurationMs", "\"DurationMs\" >= 0");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.AgentName).IsRequired().HasMaxLength(AgentNameMaxLength);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(20);
        builder.Property(s => s.InputJson).HasColumnType("jsonb");
        builder.Property(s => s.OutputJson).HasColumnType("jsonb");
        builder.Property(s => s.Retries).HasDefaultValue(0);

        // CASCADE: a step is meaningless without its run.
        builder.HasOne(s => s.Run).WithMany(r => r.Steps).HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);

        // Also serves as the RunId FK index.
        builder.HasIndex(s => new { s.RunId, s.Sequence }).IsUnique();
    }
}
