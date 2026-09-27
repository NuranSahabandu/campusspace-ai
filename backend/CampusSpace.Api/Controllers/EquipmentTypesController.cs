using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Equipment;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>Equipment types and substitutes (§9 Component B, CRUD). Any signed-in user reads them; Facilities Officers manage them.</summary>
[ApiController]
[Route("api/equipment-types")]
[Authorize]
public class EquipmentTypesController(IEquipmentTypeService types) : ControllerBase
{
    // Readable by all signed-in users: the Flutter request form lists the types to pick equipment from.
    [HttpGet]
    [ProducesResponseType<PagedResult<EquipmentTypeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<EquipmentTypeDto>>> List([FromQuery] EquipmentTypesQuery query, CancellationToken ct)
        => Ok(await types.ListAsync(query, ct));

    /// <summary>The allowed categories, for filters and forms.</summary>
    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<IReadOnlyList<string>> Categories() => Ok(EquipmentCategories.All);

    [HttpGet("{id:long}")]
    [ProducesResponseType<EquipmentTypeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EquipmentTypeDetailDto>> Get(long id, CancellationToken ct)
        => await types.GetAsync(id, ct) is { } type ? Ok(type) : NotFound();

    [HttpPost]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<EquipmentTypeDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EquipmentTypeDetailDto>> Create(EquipmentTypeRequest request, CancellationToken ct)
    {
        var type = await types.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = type.Id }, type);
    }

    /// <summary>The code cannot change (400 on Code).</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<EquipmentTypeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EquipmentTypeDetailDto>> Update(long id, EquipmentTypeRequest request, CancellationToken ct)
        => await types.UpdateAsync(id, request, ct) is { } type ? Ok(type) : NotFound();

    /// <summary>A type that still has items or reservations returns 409 "In use". Its substitute pairs are removed with it.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await types.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>The types that can replace this one, ordered by code.</summary>
    [HttpGet("{id:long}/substitutes")]
    [ProducesResponseType<IReadOnlyList<EquipmentTypeRefDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EquipmentTypeRefDto>>> GetSubstitutes(long id, CancellationToken ct)
        => await types.GetSubstitutesAsync(id, ct) is { } substitutes ? Ok(substitutes) : NotFound();

    /// <summary>Replaces the whole substitute set and returns it.</summary>
    [HttpPut("{id:long}/substitutes")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<IReadOnlyList<EquipmentTypeRefDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EquipmentTypeRefDto>>> ReplaceSubstitutes(
        long id, ReplaceSubstitutesRequest request, CancellationToken ct)
        => await types.ReplaceSubstitutesAsync(id, request, ct) is { } substitutes ? Ok(substitutes) : NotFound();
}
