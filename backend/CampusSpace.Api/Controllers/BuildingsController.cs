using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>Buildings (§9 Component A reference data). Any signed-in user reads them; Facilities Officers manage them.</summary>
[ApiController]
[Route("api/buildings")]
[Authorize]
public class BuildingsController(IBuildingService buildings) : ControllerBase
{
    // Readable by all signed-in users: Flutter and React filters need the lists.
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BuildingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<BuildingDto>>> List(CancellationToken ct)
        => Ok(await buildings.ListAsync(ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<BuildingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BuildingDto>> Get(long id, CancellationToken ct)
        => await buildings.GetAsync(id, ct) is { } building ? Ok(building) : NotFound();

    [HttpPost]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<BuildingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingDto>> Create(CreateBuildingRequest request, CancellationToken ct)
    {
        var building = await buildings.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = building.Id }, building);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<BuildingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingDto>> Update(long id, UpdateBuildingRequest request, CancellationToken ct)
        => await buildings.UpdateAsync(id, request, ct) is { } building ? Ok(building) : NotFound();

    [HttpDelete("{id:long}")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await buildings.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
