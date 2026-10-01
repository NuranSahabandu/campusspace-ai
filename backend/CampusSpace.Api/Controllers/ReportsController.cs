using CampusSpace.Api.Dtos.Reports;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// Utilization and demand reports and the dashboard KPIs (UC22, plan §9 and §12). Facilities Officers only. Read-only.
/// Opening hours and the room active flag are today's values (their history isn't stored).
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = Roles.FacilitiesOfficer)]
public class ReportsController(IReportService reports) : ControllerBase
{
    /// <summary>Booked ÷ available hours per room, per building and overall in the campus-date range.</summary>
    [HttpGet("utilization")]
    [ProducesResponseType<UtilizationReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UtilizationReportDto>> Utilization([FromQuery] ReportRangeQuery query, CancellationToken ct)
        => Ok(await reports.GetUtilizationAsync(query.From!.Value, query.To!.Value, ct));

    /// <summary>Requests per day and per requested hour, and approval outcomes, in the campus-date range.</summary>
    [HttpGet("demand")]
    [ProducesResponseType<DemandReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DemandReportDto>> Demand([FromQuery] ReportRangeQuery query, CancellationToken ct)
        => Ok(await reports.GetDemandAsync(query.From!.Value, query.To!.Value, ct));

    /// <summary>Dashboard KPIs and charts: pending approvals now, today's bookings, and the last 7 campus days.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DashboardDto>> Dashboard(CancellationToken ct)
        => Ok(await reports.GetDashboardAsync(ct));
}
