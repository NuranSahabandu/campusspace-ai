using CampusSpace.Api.Dtos.Approvals;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>The approval queue (UC17, Component D). Facilities Officers only; the decisions live on booking-requests.</summary>
[ApiController]
[Route("api/approvals")]
[Authorize(Roles = Roles.FacilitiesOfficer)]
public class ApprovalsController(IApprovalQueueService queue) : ControllerBase
{
    /// <summary>Requests pending approval, oldest pending first by default.</summary>
    [HttpGet("queue")]
    [ProducesResponseType<PagedResult<ApprovalQueueItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ApprovalQueueItemDto>>> Queue([FromQuery] ApprovalQueueQuery query, CancellationToken ct)
        => Ok(await queue.ListAsync(query, ct));
}
