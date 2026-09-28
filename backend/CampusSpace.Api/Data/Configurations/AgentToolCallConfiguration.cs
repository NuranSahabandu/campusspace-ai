using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class AgentToolCallConfiguration : IEntityTypeConfiguration<AgentToolCall>
{
    public const int ToolNameMaxLength = 50;

    public void Configure(EntityTypeBuilder<AgentToolCall> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AgentToolCalls_ToolName_NotBlank", "btrim(\"ToolName\") <> ''");
            t.HasCheckConstraint("CK_AgentToolCalls_DurationMs", "\"DurationMs\" >= 0");
            t.HasCheckConstraint("CK_AgentToolCalls_Error", "\"Succeeded\" OR \"Error\" IS NOT NULL");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.ToolName).IsRequired().HasMaxLength(ToolNameMaxLength);
        builder.Property(c => c.ArgsJson).IsRequired().HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(c => c.ResultSummary).HasColumnType("jsonb");

        // CASCADE: a tool call is meaningless without its step.
        builder.HasOne(c => c.Step).WithMany(s => s.ToolCalls).HasForeignKey(c => c.StepId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.StepId);
    }
}
