namespace CampusSpace.Api.Agents;

/// <summary>
/// One poller step for one agent run (scoped: a fresh DbContext per run). Queued runs are started; Running runs are
/// read from the agent service and their trace, proposal and outcome copied into the 3.1 tables; the watchdog fails
/// runs that never started or ran too long. Queued and Running only: AwaitingApproval and Resuming belong to the
/// officer's decision (3.4), terminal runs never change.
/// </summary>
public interface IAgentRunSync
{
    /// <param name="lastFailure">The last outage detail the poller saw for this run (for example "HTTP 401"), or null.</param>
    /// <returns>This step's outage detail when the agent service was unavailable, otherwise null.</returns>
    Task<string?> ProcessAsync(Guid runId, string? lastFailure, CancellationToken ct = default);
}
