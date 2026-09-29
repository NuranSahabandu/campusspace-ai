namespace CampusSpace.Api.Agents;

/// <summary>
/// One poller step for one agent run (scoped: a fresh DbContext per run). Queued runs are started; Running runs are
/// read from the agent service and their trace, proposal and outcome copied into the 3.1 tables; Resuming runs (after an
/// officer's approve or revise) are finished from the saved decision: the approval through IApprovalFinalizer, a revision
/// back to PendingApproval, a lost resume re-sent. The watchdog fails runs that never started or ran too long, and a live
/// run whose request doesn't match its status is failed as orphaned. AwaitingApproval runs wait for the officer; terminal
/// runs never change.
/// </summary>
public interface IAgentRunSync
{
    /// <param name="lastFailure">The last outage detail the poller saw for this run (for example "HTTP 401"), or null.</param>
    /// <returns>This step's outage detail when the agent service was unavailable, otherwise null.</returns>
    Task<string?> ProcessAsync(Guid runId, string? lastFailure, CancellationToken ct = default);
}
