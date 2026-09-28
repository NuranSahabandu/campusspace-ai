namespace CampusSpace.Api.Agents;

/// <summary>
/// .NET → agent service (§7.1 rule 2, §10.12). Every call sends X-Service-Key. None of these throw for an agent-side
/// answer or an outage: the outcome says what happened and the caller decides. Only the caller's own cancellation
/// throws.
/// </summary>
public interface IAgentClient
{
    /// <summary>POST /workflows. Ok (202), AlreadyExists (409: the thread was started before) or Unavailable.</summary>
    Task<AgentCallResult<AgentWorkflowAccepted>> StartAsync(Guid threadId, long requestId, CancellationToken ct = default);

    /// <summary>GET /workflows/{thread_id}. Ok, NotFound (404) or Unavailable. Retried (it is idempotent).</summary>
    Task<AgentCallResult<AgentWorkflowView>> GetAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>
    /// POST /workflows/{thread_id}/resume. Ok (202), NotFound, Conflict (409: not awaiting approval) or Unavailable.
    /// <paramref name="decision"/> is one of <see cref="AgentDecisions"/>. Never retried.
    /// </summary>
    Task<AgentCallResult<AgentWorkflowAccepted>> ResumeAsync(
        Guid threadId, string decision, string? notes, CancellationToken ct = default);
}

public enum AgentCallOutcome
{
    Ok,
    AlreadyExists,
    NotFound,
    Conflict,
    /// <summary>Network error, timeout, a 5xx, or a 4xx that means .NET is misconfigured (for example 401: wrong key).</summary>
    Unavailable,
}

/// <summary><paramref name="Detail"/> is a short reason for Unavailable (for example "HTTP 401" or "timeout").</summary>
public sealed record AgentCallResult<T>(AgentCallOutcome Outcome, T? Value = default, string? Detail = null)
{
    public bool IsOk => Outcome == AgentCallOutcome.Ok;

    public static AgentCallResult<T> Unavailable(string detail) => new(AgentCallOutcome.Unavailable, default, detail);
}
