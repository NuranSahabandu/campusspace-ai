using CampusSpace.Api.Models;

namespace CampusSpace.Api.Agents;

/// <summary>
/// Creates agent runs and starts them on the agent service. Used by submit, retry-agent and the poller (which retries
/// the start of a run left Queued). Depends only on the database, IAgentClient and the clock, so any service can use it.
/// </summary>
public interface IAgentRunStarter
{
    /// <summary>
    /// Adds a Queued run for <paramref name="request"/> to the context (not saved). Its Id is the LangGraph thread_id,
    /// generated here, and its RevisionNo is the request's highest RevisionNo + 1 (1 for a new request). Call it inside
    /// the transaction that holds the request's lock.
    /// </summary>
    Task<AgentRun> AddRunAsync(BookingRequest request, CancellationToken ct = default);

    /// <summary>
    /// Best-effort start: POST /workflows, then (Ok or AlreadyExists) Queued → Running with StartedAt, under the request
    /// and run locks. Any other outcome leaves the run Queued for the poller. <paramref name="timeout"/> caps the wait
    /// (submit and retry-agent pass AgentService:InlineStartTimeoutSeconds); running out is Unavailable, not an error.
    /// </summary>
    Task<AgentCallResult<AgentWorkflowAccepted>> TryStartAsync(
        Guid runId, long requestId, TimeSpan? timeout = null, CancellationToken ct = default);
}
