using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    /// <summary>
    /// The exclusion constraint of plan §8.2, added with raw SQL in the AddBookings migration (EF can't model it).
    /// A 23P01 on it maps to 409 "Time slot was just booked".
    /// </summary>
    public const string NoRoomOverlapConstraint = "no_room_overlap";

    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable(t =>
        {
            // The same shape as RoomBlackouts, so overlap checks between the two agree.
            t.HasCheckConstraint("CK_Bookings_TimeRange",
                "NOT isempty(\"TimeRange\") AND NOT lower_inf(\"TimeRange\") AND NOT upper_inf(\"TimeRange\") " +
                "AND lower_inc(\"TimeRange\") AND NOT upper_inc(\"TimeRange\")");
            t.HasCheckConstraint("CK_Bookings_Status",
                $"\"Status\" IN ({string.Join(", ", BookingStatuses.All.Select(s => $"'{s}'"))})");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.TimeRange).IsRequired().HasColumnType("tstzrange");
        builder.Property(b => b.Status).IsRequired().HasMaxLength(20);

        // One booking per request; the UNIQUE index is also the RequestId FK index.
        builder.HasOne(b => b.Request).WithOne().HasForeignKey<Booking>(b => b.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Room).WithMany().HasForeignKey(b => b.RoomId).OnDelete(DeleteBehavior.Restrict);

        // The exclusion constraint's GiST index is partial (active bookings only), so the FK needs its own index.
        builder.HasIndex(b => b.RoomId);
    }
}
