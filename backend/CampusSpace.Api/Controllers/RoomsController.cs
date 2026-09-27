using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// Rooms (§9 Component A): CRUD, availability search and day schedule. Any signed-in user searches active rooms;
/// Facilities Officers manage them.
/// </summary>
[ApiController]
[Route("api/rooms")]
[Authorize]
public class RoomsController(IRoomService rooms, IRoomAvailabilityService availability) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<RoomDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<RoomDto>>> List([FromQuery] RoomsQuery query, CancellationToken ct)
        => Ok(await rooms.ListAsync(query, ct));

    /// <summary>
    /// Business op: active rooms free for [start, end) that seat at least minCapacity (and at most maxCapacity) and have
    /// ALL the features. The slot must follow the opening-hours, granularity and duration rules (V05). Best fit first.
    /// </summary>
    [HttpGet("availability")]
    [ProducesResponseType<PagedResult<RoomDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<RoomDto>>> Availability([FromQuery] RoomAvailabilityQuery query, CancellationToken ct)
    {
        var criteria = new AvailabilityCriteria(query.Start!.Value, query.End!.Value, query.MinCapacity!.Value,
            FeatureCodeList.Parse(query.Features), query.BuildingId, query.Type, query.MaxCapacity);
        return Ok(await availability.FindAvailableAsync(criteria, query, ct));
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType<RoomDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomDto>> Get(long id, CancellationToken ct)
        => await rooms.GetAsync(id, ct) is { } room ? Ok(room) : NotFound();

    [HttpPost]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<RoomDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomDto>> Create(CreateRoomRequest request, CancellationToken ct)
    {
        var room = await rooms.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = room.Id }, room);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<RoomDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomDto>> Update(long id, UpdateRoomRequest request, CancellationToken ct)
        => await rooms.UpdateAsync(id, request, ct) is { } room ? Ok(room) : NotFound();

    /// <summary>Soft delete: the room is deactivated and hidden from everyone but Facilities Officers.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await rooms.DeactivateAsync(id, ct) ? NoContent() : NotFound();
}
