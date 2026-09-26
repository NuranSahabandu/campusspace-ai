using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class RoomBlackoutConfiguration : IEntityTypeConfiguration<RoomBlackout>
{
    /// <summary>GiST on (RoomId, TimeRange) for overlap queries. Needs btree_gist for the bigint column.</summary>
    public const string RoomTimeRangeIndex = "IX_RoomBlackouts_RoomId_TimeRange";

    public void Configure(EntityTypeBuilder<RoomBlackout> builder)
    {
        builder.ToTable(t =>
        {
            // Finite, non-empty and [start, end): the same shape as bookings, so overlap checks agree.
            t.HasCheckConstraint("CK_RoomBlackouts_TimeRange",
                "NOT isempty(\"TimeRange\") AND NOT lower_inf(\"TimeRange\") AND NOT upper_inf(\"TimeRange\") " +
                "AND lower_inc(\"TimeRange\") AND NOT upper_inc(\"TimeRange\")");
            t.HasCheckConstraint("CK_RoomBlackouts_Reason_NotBlank", "btrim(\"Reason\") <> ''");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.TimeRange).IsRequired().HasColumnType("tstzrange");
        builder.Property(b => b.Reason).IsRequired().HasMaxLength(200);

        builder.HasOne(b => b.Room).WithMany(r => r.Blackouts).HasForeignKey(b => b.RoomId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(b => b.CreatedBy).WithMany().HasForeignKey(b => b.CreatedById).OnDelete(DeleteBehavior.Restrict);

        // Also serves as the RoomId FK index (RoomId is its leading column).
        builder.HasIndex(b => new { b.RoomId, b.TimeRange }, RoomTimeRangeIndex).HasMethod("gist");
        builder.HasIndex(b => b.CreatedById);
    }
}
