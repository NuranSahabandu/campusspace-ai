using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class RequestStatusHistoryConfiguration : IEntityTypeConfiguration<RequestStatusHistory>
{
    public const int ReasonMaxLength = 500;

    public void Configure(EntityTypeBuilder<RequestStatusHistory> builder)
    {
        builder.ToTable("RequestStatusHistory", t =>
        {
            t.HasCheckConstraint("CK_RequestStatusHistory_FromStatus",
                $"\"FromStatus\" IS NULL OR \"FromStatus\" IN ({RequestStatusSql.InList})");
            t.HasCheckConstraint("CK_RequestStatusHistory_ToStatus", $"\"ToStatus\" IN ({RequestStatusSql.InList})");
        });

        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasMaxLength(20);
        builder.Property(h => h.ToStatus).IsRequired().HasMaxLength(20);
        builder.Property(h => h.Reason).HasMaxLength(ReasonMaxLength);
        builder.Property(h => h.ChangedAt).IsRequired();

        // RESTRICT: requests are never deleted, and their history must not disappear with them.
        builder.HasOne(h => h.Request).WithMany(r => r.StatusHistory).HasForeignKey(h => h.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.ChangedBy).WithMany().HasForeignKey(h => h.ChangedById).OnDelete(DeleteBehavior.Restrict);

        // The timeline, oldest first. Also serves as the RequestId FK index.
        builder.HasIndex(h => new { h.RequestId, h.ChangedAt });
        builder.HasIndex(h => h.ChangedById);
    }
}
