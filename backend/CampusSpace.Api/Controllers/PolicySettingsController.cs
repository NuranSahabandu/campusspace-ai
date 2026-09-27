using CampusSpace.Api.Dtos.Policy;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>Booking policy (addendum A.1, UC29). Facilities Officers read and change it; everyone signed in reads the values.</summary>
[ApiController]
[Route("api/policy-settings")]
[Authorize(Roles = Roles.FacilitiesOfficer)]
public class PolicySettingsController(IPolicySettingsService policy) : ControllerBase
{
    /// <summary>Every setting with its type, description and who changed it last.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PolicySettingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PolicySettingDto>>> List(CancellationToken ct)
        => Ok(await policy.ListAsync(ct));

    /// <summary>
    /// Changes the submitted keys. The whole policy is checked with the new values; errors are keyed by setting key.
    /// Each changed key writes one audit row with its old and new value.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<IReadOnlyList<PolicySettingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PolicySettingDto>>> Update(PolicySettingsUpdateRequest request, CancellationToken ct)
        => Ok(await policy.UpdateAsync(request, ct));

    /// <summary>
    /// The current values only (key to typed value), with no descriptions or editor details. Any signed-in user: the
    /// Flutter request form uses it to guide its date and time pickers (lead time, advance window, opening hours,
    /// granularity). This is the addendum's Open question 1, option (a). Built from the same snapshot as every other
    /// consumer, so it always agrees with the officer view.
    /// </summary>
    [HttpGet("public")]
    [Authorize]
    [ProducesResponseType<IReadOnlyDictionary<string, object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyDictionary<string, object?>>> Public(CancellationToken ct)
        => Ok((await policy.GetAsync(ct)).ToPublicValues());
}
