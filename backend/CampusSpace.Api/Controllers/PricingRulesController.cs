using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Pricing;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// Room pricing rules (§8.1 Component D). Facilities Officers only. Rules already in effect are read-only: change a
/// price by adding a rule with a later ValidFrom.
/// </summary>
[ApiController]
[Route("api/pricing-rules")]
[Authorize(Roles = Roles.FacilitiesOfficer)]
public class PricingRulesController(IPricingRuleService rules) : ControllerBase
{
    /// <summary>Rules in effect now and scheduled ones; includeHistory=true adds superseded rules.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<PricingRuleDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<PricingRuleDto>>> List([FromQuery] PricingRulesQuery query, CancellationToken ct)
        => Ok(await rules.ListAsync(query, ct));

    /// <summary>One row per room type and requester role, with the rule in effect today or nulls for a gap.</summary>
    [HttpGet("current")]
    [ProducesResponseType<IReadOnlyList<CurrentPricingRuleDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<CurrentPricingRuleDto>>> Current(CancellationToken ct)
        => Ok(await rules.GetCurrentAsync(ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<PricingRuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PricingRuleDto>> Get(long id, CancellationToken ct)
        => await rules.GetAsync(id, ct) is { } rule ? Ok(rule) : NotFound();

    /// <summary>ValidFrom must be today or later (campus time). A duplicate room type, role and date is a 409.</summary>
    [HttpPost]
    [ProducesResponseType<PricingRuleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PricingRuleDto>> Create(PricingRuleRequest request, CancellationToken ct)
    {
        var rule = await rules.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = rule.Id }, rule);
    }

    /// <summary>Only a scheduled rule can change (400 on ValidFrom otherwise).</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<PricingRuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PricingRuleDto>> Update(long id, PricingRuleRequest request, CancellationToken ct)
        => await rules.UpdateAsync(id, request, ct) is { } rule ? Ok(rule) : NotFound();

    /// <summary>Only a scheduled rule can be deleted (400 on ValidFrom otherwise).</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await rules.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
