using CampusSpace.Api.Auth;
using CampusSpace.Api.Dtos.AgentTools;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Dtos.Quotations;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Internal;

/// <summary>
/// Read-only tool routes for the agent service (§9 "Internal read-only tool endpoints", §7.1 rule 3). X-Agent-Key only
/// (policy AgentTools): a user JWT gets a 401 here, and the agent key gets a 401 on /api. Hidden from Swagger. Every
/// route is a GET except POST quote, which calculates and never saves. Times need an explicit offset. Errors are
/// Problem Details (400/404), which the Python tools turn into TOOL_ERROR observations.
/// </summary>
[ApiController]
[Route("internal/agent-tools")]
[Authorize(Policy = AgentKeyDefaults.Policy)]
public class AgentToolsController(IAgentToolService tools) : ControllerBase
{
    /// <summary>Supervisor: the request, the requester's role and club status, and V11 counts. No personal data.</summary>
    [HttpGet("request-context/{requestId:long}")]
    [ProducesResponseType<AgentRequestContextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentRequestContextDto>> RequestContext(long requestId, CancellationToken ct)
        => await tools.GetRequestContextAsync(requestId, ct) is { } context ? Ok(context) : NotFound();

    /// <summary>Supervisor: the only feature codes a plan may use.</summary>
    [HttpGet("catalog/features")]
    [ProducesResponseType<IReadOnlyList<AgentFeatureDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentFeatureDto>>> Features(CancellationToken ct)
        => Ok(await tools.ListFeaturesAsync(ct));

    /// <summary>Supervisor: the only equipment codes a plan may use, with fees and covering features.</summary>
    [HttpGet("catalog/equipment")]
    [ProducesResponseType<IReadOnlyList<AgentEquipmentTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentEquipmentTypeDto>>> Equipment(CancellationToken ct)
        => Ok(await tools.ListEquipmentAsync(ct));

    /// <summary>Venue Matching: active rooms free for [start, end), best fit first, paged with a total.</summary>
    [HttpGet("rooms/available")]
    [ProducesResponseType<PagedResult<RoomDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<RoomDto>>> AvailableRooms([FromQuery] AgentRoomsQuery query, CancellationToken ct)
        => Ok(await tools.FindAvailableRoomsAsync(query, ct));

    /// <summary>Venue Matching: one active room with its features.</summary>
    [HttpGet("rooms/{id:long}")]
    [ProducesResponseType<RoomDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomDto>> Room(long id, CancellationToken ct)
        => await tools.GetRoomAsync(id, ct) is { } room ? Ok(room) : NotFound();

    /// <summary>Equipment Allocation: serviceable, reserved and available counts per code for [start, end).</summary>
    [HttpGet("equipment/availability")]
    [ProducesResponseType<IReadOnlyList<AgentEquipmentAvailabilityDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<AgentEquipmentAvailabilityDto>>> EquipmentAvailability(
        [FromQuery] AgentEquipmentAvailabilityQuery query, CancellationToken ct)
        => Ok(await tools.GetEquipmentAvailabilityAsync(query, ct));

    /// <summary>Equipment Allocation: the types that can replace this one (ask availability for them separately).</summary>
    [HttpGet("equipment/substitutes/{code}")]
    [ProducesResponseType<IReadOnlyList<AgentEquipmentRefDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AgentEquipmentRefDto>>> Substitutes(string code, CancellationToken ct)
        => await tools.GetSubstitutesAsync(code, ct) is { } substitutes ? Ok(substitutes) : NotFound();

    /// <summary>Policy and Cost: the price of a slot and equipment from IQuotationCalculator. Calculates only; never saves.</summary>
    [HttpPost("quote")]
    [ProducesResponseType<QuotationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationDto>> Quote(AgentQuoteRequest request, CancellationToken ct)
        => await tools.QuoteAsync(request, ct) is { } quote ? Ok(quote) : NotFound();

    /// <summary>
    /// The full policy snapshot (addendum A.1/A.2): every key with its typed value, the same as
    /// GET /api/policy-settings/public. The agent service reads it once at the start of a run.
    /// </summary>
    [HttpGet("policy")]
    [ProducesResponseType<IReadOnlyDictionary<string, object>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyDictionary<string, object?>>> Policy(CancellationToken ct)
        => Ok(await tools.GetPolicyAsync(ct));
}
