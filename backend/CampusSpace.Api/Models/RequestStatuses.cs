namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of booking request statuses (§8.3). Used by the CHECK constraints on BookingRequests and
/// RequestStatusHistory and by RequestStateMachine, which is the only code that changes a request's status.
/// </summary>
public static class RequestStatuses
{
    public const string Submitted = "Submitted";
    public const string AgentProcessing = "AgentProcessing";
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Completed = "Completed";
    public const string AgentFailed = "AgentFailed";
    public const string RevisionRequested = "RevisionRequested";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All =
    [
        Submitted, AgentProcessing, PendingApproval, Approved, Completed,
        AgentFailed, RevisionRequested, Rejected, Cancelled,
    ];

    /// <summary>
    /// The statuses that count toward max_open_requests (V11): the request is still being worked on. Approved is not
    /// open because it is a confirmed booking. AgentFailed waits on an officer retry, not on the requester, so it is
    /// closed for the cap, as are the terminal states.
    /// </summary>
    public static readonly IReadOnlyList<string> Open = [Submitted, AgentProcessing, PendingApproval, RevisionRequested];

    /// <summary>No transition leaves these.</summary>
    public static readonly IReadOnlyList<string> Terminal = [Completed, Rejected, Cancelled];
}
