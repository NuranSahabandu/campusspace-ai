using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>Maintenance blackouts of a room (§9 Component A). Facilities Officers only.</summary>
[ApiController]
[Route("api/rooms/{roomId:long}/blackouts")]
[Authorize(Roles = Roles.FacilitiesOfficer)]
public class RoomBlackoutsController(IRoomBlackoutService blackouts) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<BlackoutDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<BlackoutDto>>> List(long roomId, [FromQuery] BlackoutsQuery query, CancellationToken ct)
        => await blackouts.ListAsync(roomId, query, ct) is { } page ? Ok(page) : NotFound();

    [HttpGet("{blackoutId:long}")]
    [ProducesResponseType<BlackoutDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BlackoutDto>> Get(long roomId, long blackoutId, CancellationToken ct)
        => await blackouts.GetAsync(roomId, blackoutId, ct) is { } blackout ? Ok(blackout) : NotFound();

    /// <summary>
    /// Business op (UC14): adds the blackout and returns it with the Active bookings it clashes with. Clashing bookings
    /// are not cancelled; the officer handles them.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<BlackoutWithClashesDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BlackoutWithClashesDto>> Create(long roomId, CreateBlackoutRequest request, CancellationToken ct)
        => await blackouts.CreateAsync(roomId, request, ct) is { } blackout
            ? CreatedAtAction(nameof(Get), new { roomId, blackoutId = blackout.Id }, blackout)
            : NotFound();

    /// <summary>The Active bookings of the room that overlap the blackout, by start time (for refreshing the warning).</summary>
    [HttpGet("{blackoutId:long}/clashes")]
    [ProducesResponseType<IReadOnlyList<BlackoutClashDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<BlackoutClashDto>>> Clashes(long roomId, long blackoutId, CancellationToken ct)
        => await blackouts.GetClashesAsync(roomId, blackoutId, ct) is { } clashes ? Ok(clashes) : NotFound();

    [HttpDelete("{blackoutId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long roomId, long blackoutId, CancellationToken ct)
        => await blackouts.DeleteAsync(roomId, blackoutId, ct) ? NoContent() : NotFound();
}
