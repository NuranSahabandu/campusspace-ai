using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class EquipmentReservationConfiguration : IEntityTypeConfiguration<EquipmentReservation>
{
    /// <summary>GiST on (TypeId, TimeRange) for overlap counts. Needs btree_gist for the bigint column.</summary>
    public const string TypeTimeRangeIndex = "IX_EquipmentReservations_TypeId_TimeRange";

    public void Configure(EntityTypeBuilder<EquipmentReservation> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_EquipmentReservations_Quantity", "\"Quantity\" > 0");
            // A copy of the booking's range, so it has the same shape as CK_Bookings_TimeRange.
            t.HasCheckConstraint("CK_EquipmentReservations_TimeRange",
                "NOT isempty(\"TimeRange\") AND NOT lower_inf(\"TimeRange\") AND NOT upper_inf(\"TimeRange\") " +
                "AND lower_inc(\"TimeRange\") AND NOT upper_inc(\"TimeRange\")");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.TimeRange).IsRequired().HasColumnType("tstzrange");

        // A reservation means nothing without its booking; an equipment type can't be deleted while reserved (409 "In use").
        builder.HasOne(r => r.Booking).WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Type).WithMany().HasForeignKey(r => r.TypeId).OnDelete(DeleteBehavior.Restrict);

        // One row per type per booking; also the BookingId FK index (BookingId is its leading column).
        builder.HasIndex(r => new { r.BookingId, r.TypeId }).IsUnique();
        // Also serves as the TypeId FK index (TypeId is its leading column).
        builder.HasIndex(r => new { r.TypeId, r.TimeRange }, TypeTimeRangeIndex).HasMethod("gist");
    }
}
