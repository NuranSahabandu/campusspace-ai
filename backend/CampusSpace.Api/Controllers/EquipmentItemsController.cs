using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Equipment;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// Equipment items (§9 Component B, CRUD) for Facilities Officers and Lab Technicians. There is no DELETE:
/// items are retired through Status.
/// </summary>
[ApiController]
[Route("api/equipment-items")]
[Authorize(Roles = $"{Roles.FacilitiesOfficer},{Roles.LabTechnician}")]
public class EquipmentItemsController(IEquipmentItemService items) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<EquipmentItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<EquipmentItemDto>>> List([FromQuery] EquipmentItemsQuery query, CancellationToken ct)
        => Ok(await items.ListAsync(query, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<EquipmentItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EquipmentItemDto>> Get(long id, CancellationToken ct)
        => await items.GetAsync(id, ct) is { } item ? Ok(item) : NotFound();

    [HttpPost]
    [ProducesResponseType<EquipmentItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EquipmentItemDto>> Create(EquipmentItemRequest request, CancellationToken ct)
    {
        var item = await items.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    /// <summary>Status OnLoan cannot be set here, an item on loan accepts only a Notes change, and the type cannot change.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<EquipmentItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EquipmentItemDto>> Update(long id, EquipmentItemRequest request, CancellationToken ct)
        => await items.UpdateAsync(id, request, ct) is { } item ? Ok(item) : NotFound();
}
