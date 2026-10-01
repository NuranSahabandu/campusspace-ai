using CampusSpace.Api.Dtos.AgentRuns;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// The agent audit trail (UC18) and the runs monitor (UC23). Facilities Officers only: requesters see just the
/// LatestProposal summary on their request, never the trace, plan or policy snapshot.
/// </summary>
[ApiController]
[Route("api/agent-runs")]
[Authorize(Roles = Roles.FacilitiesOfficer)]
public class AgentRunsController(IAgentRunReadService runs, IAgentRunMonitorService monitor) : ControllerBase
{
    /// <summary>Every run, newest first, with filters (status, campus-date range on CreatedAt, request, fallback, purpose).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AgentRunListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AgentRunListItemDto>>> List([FromQuery] AgentRunsQuery query, CancellationToken ct)
        => Ok(await monitor.ListAsync(query, ct));

    /// <summary>Per-agent and per-run metrics for the runs created in the campus-date range, with denominators.</summary>
    [HttpGet("metrics")]
    [ProducesResponseType<AgentRunMetricsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AgentRunMetricsDto>> Metrics([FromQuery] AgentRunMetricsQuery query, CancellationToken ct)
        => Ok(await monitor.GetMetricsAsync(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AgentRunDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentRunDetailDto>> Get(Guid id, CancellationToken ct)
        => await runs.GetAsync(id, ct) is { } run ? Ok(run) : NotFound();

    /// <summary>The request's runs, newest first.</summary>
    [HttpGet("~/api/booking-requests/{requestId:long}/agent-runs")]
    [ProducesResponseType<IReadOnlyList<AgentRunSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AgentRunSummaryDto>>> ForRequest(long requestId, CancellationToken ct)
        => await runs.ListForRequestAsync(requestId, ct) is { } list ? Ok(list) : NotFound();
}
