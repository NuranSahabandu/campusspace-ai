namespace CampusSpace.Api.Models;

/// <summary>
/// A requester's booking objective (§8.1 Component C). Times are UTC [RequestedStart, RequestedEnd).
/// Status changes only through IRequestStateMachine, which also writes StatusHistory.
/// Notes is untrusted requester text: stored as given, never interpreted and never logged by value.
/// </summary>
public class BookingRequest : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long RequesterId { get; set; }
    public User Requester { get; set; } = null!;
    /// <summary>The club a Student books for (V11). Null for a Lecturer's academic booking.</summary>
    public long? ClubId { get; set; }
    public Club? Club { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public int Attendees { get; set; }
    public DateTime RequestedStart { get; set; }
    public DateTime RequestedEnd { get; set; }
    public decimal BudgetLkr { get; set; }
    /// <summary>Feature codes (projector, computers). Checked against Features when submitted.</summary>
    public List<string> RequiredFeatures { get; set; } = [];
    public string? Notes { get; set; }
    /// <summary>One of <see cref="RequestStatuses.All"/>.</summary>
    public string Status { get; set; } = RequestStatuses.Submitted;
    /// <summary>When the request was cancelled through the cancel operation (UC07). Null otherwise.</summary>
    public DateTime? CancelledAt { get; set; }
    /// <summary>
    /// The owner cancelled an Approved booking later than free_cancellation_hours before its start. Flagged, not charged.
    /// </summary>
    public bool IsLateCancellation { get; set; }
    /// <summary>A Facilities Officer cancelled it (never late: the requester isn't at fault).</summary>
    public bool CancelledByOfficer { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<RequestedEquipmentLine> EquipmentLines { get; set; } = [];
    public List<RequestStatusHistory> StatusHistory { get; set; } = [];
}
