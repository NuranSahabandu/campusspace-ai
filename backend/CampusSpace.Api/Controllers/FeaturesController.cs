using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>Room features (§9 Component A reference data). The codes are what the agents use. Any signed-in user reads them; Facilities Officers manage them.</summary>
[ApiController]
[Route("api/features")]
[Authorize]
public class FeaturesController(IFeatureService features) : ControllerBase
{
    // Readable by all signed-in users: Flutter and React filters need the lists.
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FeatureDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<FeatureDto>>> List(CancellationToken ct)
        => Ok(await features.ListAsync(ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<FeatureDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FeatureDto>> Get(long id, CancellationToken ct)
        => await features.GetAsync(id, ct) is { } feature ? Ok(feature) : NotFound();

    [HttpPost]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<FeatureDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FeatureDto>> Create(FeatureRequest request, CancellationToken ct)
    {
        var feature = await features.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = feature.Id }, feature);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<FeatureDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FeatureDto>> Update(long id, FeatureRequest request, CancellationToken ct)
        => await features.UpdateAsync(id, request, ct) is { } feature ? Ok(feature) : NotFound();

    [HttpDelete("{id:long}")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await features.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
