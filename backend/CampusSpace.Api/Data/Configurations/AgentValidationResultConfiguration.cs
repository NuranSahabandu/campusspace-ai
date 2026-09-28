using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class AgentValidationResultConfiguration : IEntityTypeConfiguration<AgentValidationResult>
{
    public void Configure(EntityTypeBuilder<AgentValidationResult> builder)
    {
        builder.ToTable("ValidationResults", t =>
        {
            t.HasCheckConstraint("CK_ValidationResults_Attempt", "\"Attempt\" > 0");
            t.HasCheckConstraint("CK_ValidationResults_RuleCode", "\"RuleCode\" ~ '^V(0[1-9]|1[0-2])$'");
        });

        builder.HasKey(v => v.Id);
        builder.Property(v => v.RuleCode).IsRequired().HasMaxLength(3);

        // CASCADE: a rule outcome is meaningless without its run.
        builder.HasOne(v => v.Run).WithMany(r => r.ValidationResults).HasForeignKey(v => v.RunId).OnDelete(DeleteBehavior.Cascade);

        // One outcome per rule per validation pass. Also serves as the RunId FK index.
        builder.HasIndex(v => new { v.RunId, v.Attempt, v.RuleCode }).IsUnique();
    }
}
