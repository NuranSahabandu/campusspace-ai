using NpgsqlTypes;

namespace CampusSpace.Api.Models;

/// <summary>
/// A period when a room cannot be booked (for example maintenance). TimeRange is a UTC tstzrange,
/// lower-inclusive and upper-exclusive ([start, end)), enforced by CK_RoomBlackouts_TimeRange.
/// </summary>
public class RoomBlackout : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public NpgsqlRange<DateTime> TimeRange { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
