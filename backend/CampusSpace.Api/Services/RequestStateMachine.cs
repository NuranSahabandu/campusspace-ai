using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

public sealed class RequestStateMachine(TimeProvider clock) : IRequestStateMachine
{
    /// <summary>
    /// From → allowed targets. Revision loops back through AgentProcessing; AgentFailed is retried by an officer
    /// (Phase 5). Cancel is allowed from Submitted, PendingApproval and Approved (§8.3). Terminal states have no exits.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTransitions =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [RequestStatuses.Submitted] = new HashSet<string> { RequestStatuses.AgentProcessing, RequestStatuses.Cancelled },
            [RequestStatuses.AgentProcessing] = new HashSet<string> { RequestStatuses.PendingApproval, RequestStatuses.AgentFailed },
            [RequestStatuses.PendingApproval] = new HashSet<string>
            {
                RequestStatuses.Approved, RequestStatuses.Rejected, RequestStatuses.RevisionRequested, RequestStatuses.Cancelled,
            },
            [RequestStatuses.RevisionRequested] = new HashSet<string> { RequestStatuses.AgentProcessing },
            [RequestStatuses.AgentFailed] = new HashSet<string> { RequestStatuses.AgentProcessing },
            [RequestStatuses.Approved] = new HashSet<string> { RequestStatuses.Completed, RequestStatuses.Cancelled },
            [RequestStatuses.Completed] = new HashSet<string>(),
            [RequestStatuses.Rejected] = new HashSet<string>(),
            [RequestStatuses.Cancelled] = new HashSet<string>(),
        };

    public static bool CanTransition(string from, string to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public void Start(BookingRequest request, long? changedById)
    {
        request.Status = RequestStatuses.Submitted;
        AddHistory(request, from: null, RequestStatuses.Submitted, changedById, reason: null);
    }

    public void Transition(BookingRequest request, string to, long? changedById, string? reason = null)
    {
        var from = request.Status;
        if (!CanTransition(from, to))
            throw new ConflictException($"Can't move a request from {from} to {to}");

        request.Status = to;
        AddHistory(request, from, to, changedById, reason);
    }

    private void AddHistory(BookingRequest request, string? from, string to, long? changedById, string? reason) =>
        request.StatusHistory.Add(new RequestStatusHistory
        {
            FromStatus = from,
            ToStatus = to,
            ChangedById = changedById,
            Reason = reason,
            ChangedAt = clock.GetUtcNow().UtcDateTime,
        });
}
