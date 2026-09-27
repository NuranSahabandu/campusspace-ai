using CampusSpace.Api.Dtos.Equipment;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>Equipment availability (§9 Component B business op). Any signed-in user.</summary>
[ApiController]
[Route("api/equipment")]
[Authorize]
public class EquipmentAvailabilityController(IEquipmentAvailabilityService availability) : ControllerBase
{
    /// <summary>
    /// Business op: serviceable items of the type (Available + OnLoan) minus those reserved by active bookings overlapping
    /// [start, end). The slot must follow the opening-hours, granularity and duration rules (V05).
    /// </summary>
    [HttpGet("availability")]
    [ProducesResponseType<EquipmentAvailabilityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EquipmentAvailabilityDto>> Availability([FromQuery] EquipmentAvailabilityQuery query, CancellationToken ct)
        => await availability.GetAvailabilityAsync(query.TypeId!.Value, query.Start!.Value, query.End!.Value, ct) is { } result
            ? Ok(result)
            : NotFound();
}
