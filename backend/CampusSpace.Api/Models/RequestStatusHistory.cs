namespace CampusSpace.Api.Models;

/// <summary>
/// One status change of a booking request, written by IRequestStateMachine in the same SaveChanges as the change.
/// FromStatus is null on the first row. ChangedById is null when the system made the change (later phases).
/// </summary>
public class RequestStatusHistory
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public BookingRequest Request { get; set; } = null!;
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public long? ChangedById { get; set; }
    public User? ChangedBy { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
}
