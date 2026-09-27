using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Requests;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// Booking requests (§9 Component C). Students and Lecturers submit and read their own; Facilities Officers read all.
/// Stacked [Authorize] attributes must all pass, so submit and eligibility are for requesters only.
/// </summary>
[ApiController]
[Route("api/booking-requests")]
[Authorize(Roles = $"{Roles.Student},{Roles.Lecturer},{Roles.FacilitiesOfficer}")]
public class BookingRequestsController(IBookingRequestService requests) : ControllerBase
{
    private const string Requesters = $"{Roles.Student},{Roles.Lecturer}";

    [HttpGet]
    [ProducesResponseType<PagedResult<BookingRequestSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<BookingRequestSummaryDto>>> List([FromQuery] BookingRequestsQuery query, CancellationToken ct)
        => Ok(await requests.ListAsync(query, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<BookingRequestDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingRequestDetailDto>> Get(long id, CancellationToken ct)
        => await requests.GetAsync(id, ct) is { } request ? Ok(request) : NotFound();

    [HttpGet("{id:long}/history")]
    [ProducesResponseType<IReadOnlyList<RequestStatusHistoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RequestStatusHistoryDto>>> History(long id, CancellationToken ct)
        => await requests.GetHistoryAsync(id, ct) is { } history ? Ok(history) : NotFound();

    /// <summary>Whether the caller can submit now, the clubs they can submit for, and their open-request count.</summary>
    [HttpGet("eligibility")]
    [Authorize(Roles = Requesters)]
    [ProducesResponseType<EligibilityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EligibilityDto>> Eligibility(CancellationToken ct)
        => Ok(await requests.GetEligibilityAsync(ct));

    /// <summary>Saves the request as Submitted. (Phase 3 starts the agent workflow here and returns 202.)</summary>
    [HttpPost]
    [Authorize(Roles = Requesters)]
    [ProducesResponseType<BookingRequestDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingRequestDetailDto>> Create(CreateBookingRequestRequest request, CancellationToken ct)
    {
        var created = await requests.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
