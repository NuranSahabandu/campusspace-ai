using CampusSpace.Api.Dtos.AgentRuns;

namespace CampusSpace.Api.Services;

/// <summary>Officer-only reads of the agent audit trail (UC18). Requesters get only LatestProposal on the request detail.</summary>
public interface IAgentRunReadService
{
    /// <summary>The request's runs, newest first; null when the request doesn't exist.</summary>
    Task<IReadOnlyList<AgentRunSummaryDto>?> ListForRequestAsync(long requestId, CancellationToken ct = default);

    /// <summary>One run with its proposal, trace, validation, decisions and policy snapshot; null when unknown.</summary>
    Task<AgentRunDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
}
