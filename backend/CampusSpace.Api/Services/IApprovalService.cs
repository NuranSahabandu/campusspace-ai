using CampusSpace.Api.Dtos.Requests;

namespace CampusSpace.Api.Services;

/// <summary>The request after approve: <see cref="InProgress"/> when the agent hasn't confirmed yet (202).</summary>
public sealed record ApproveResult(BookingRequestDetailDto? Detail, bool InProgress);

/// <summary>
/// A Facilities Officer's decision on a PendingApproval request (UC19–UC21, plan §10.9, §11 steps 6–7). Every decision is
/// saved (an ApprovalDecisions row) in the first transaction, before any call to the agent service, so the saved decision
/// is what the approve call and the poller act on. Null means the request doesn't exist (404). A request that isn't
/// PendingApproval, or whose live run isn't AwaitingApproval (a decision is already being made), is a 409.
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Saves the decision (run → Resuming), resumes the agent, then waits up to AgentService:ApprovalWaitSeconds for its
    /// finalize. Completed → the approval transaction (IApprovalFinalizer) and the Approved request. A failed final check is a
    /// ConflictException (409) whose title says whether the request was closed (time) or a new proposal is being prepared.
    /// No answer in time → InProgress (202); the poller finishes it.
    /// </summary>
    Task<ApproveResult?> ApproveAsync(long id, string? comment, CancellationToken ct = default);

    /// <summary>
    /// One transaction: the decision, run → Rejected, the Draft voided, request → Rejected with the reason. Then a best-effort
    /// resume "reject". A blank reason is a 400.
    /// </summary>
    Task<BookingRequestDetailDto?> RejectAsync(long id, string? reason, CancellationToken ct = default);

    /// <summary>
    /// One transaction: the decision, the Draft voided, the same run → Resuming with the next RevisionNo, request
    /// PendingApproval → RevisionRequested → AgentProcessing. Then resume "revise" with the notes (best-effort; the poller
    /// re-sends it). A blank note is a 400.
    /// </summary>
    Task<BookingRequestDetailDto?> RequestRevisionAsync(long id, string? notes, CancellationToken ct = default);
}
