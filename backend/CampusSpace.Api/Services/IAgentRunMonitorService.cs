using CampusSpace.Api.Dtos.AgentRuns;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Services;

/// <summary>The agent runs monitor (UC23, plan §10.11): every run with filters, and per-agent and per-run metrics. Read-only.</summary>
public interface IAgentRunMonitorService
{
    Task<PagedResult<AgentRunListItemDto>> ListAsync(AgentRunsQuery query, CancellationToken ct = default);

    Task<AgentRunMetricsDto> GetMetricsAsync(AgentRunMetricsQuery query, CancellationToken ct = default);
}
