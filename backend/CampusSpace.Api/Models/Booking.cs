using NpgsqlTypes;

namespace CampusSpace.Api.Models;

/// <summary>
/// A room held for an approved request (§8.1 Component C). Created only by the approval transaction (Phase 3).
/// TimeRange is a UTC tstzrange [start, end), enforced by CK_Bookings_TimeRange. Two active bookings of one room can't
/// overlap: the no_room_overlap exclusion constraint guarantees it, whatever the code checks first.
/// </summary>
public class Booking : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public BookingRequest Request { get; set; } = null!;
    public long RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public NpgsqlRange<DateTime> TimeRange { get; set; }
    /// <summary>One of <see cref="BookingStatuses.All"/>.</summary>
    public string Status { get; set; } = BookingStatuses.Confirmed;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
