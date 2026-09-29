using CampusSpace.Api.Dtos.AgentRuns;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// The agent audit trail (UC18). Facilities Officers only: requesters see just the LatestProposal summary on their
/// request, never the trace, plan or policy snapshot.
/// </summary>
[ApiController]
[Route("api/agent-runs")]
[Authorize(Roles = Roles.FacilitiesOfficer)]
public class AgentRunsController(IAgentRunReadService runs) : ControllerBase
{
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
