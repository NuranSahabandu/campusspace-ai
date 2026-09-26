using CampusSpace.Api.Dtos.AuditLogs;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>The audit log (§15.3). Read-only: rows are written by AppDbContext and IAuditService.</summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = Roles.Admin)]
public class AuditLogsController(IAuditService audit) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List([FromQuery] AuditLogsQuery query, CancellationToken ct)
        => Ok(await audit.ListAsync(query, ct));
}
