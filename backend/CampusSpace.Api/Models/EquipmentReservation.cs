using NpgsqlTypes;

namespace CampusSpace.Api.Models;

/// <summary>
/// Equipment held for a booking (§8.1 Component B): Quantity items of one type. Created only by the approval
/// transaction through IEquipmentAvailabilityService.ReserveAsync. A reservation holds equipment only while its booking
/// is in <see cref="BookingStatuses.Active"/>; a Cancelled or Completed booking's rows stay as history but hold nothing.
/// TimeRange is always a copy of the booking's TimeRange (UTC [start, end)). It is stored here only so the GiST index on
/// (TypeId, TimeRange) can serve availability queries without joining Bookings first.
/// </summary>
public class EquipmentReservation : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public long TypeId { get; set; }
    public EquipmentType Type { get; set; } = null!;
    public int Quantity { get; set; }
    public NpgsqlRange<DateTime> TimeRange { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
