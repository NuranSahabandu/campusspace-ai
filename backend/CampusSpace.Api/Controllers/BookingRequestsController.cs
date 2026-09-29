using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Requests;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CampusSpace.Api.Controllers;

/// <summary>
/// Booking requests (§9 Component C). Students and Lecturers submit, read and cancel their own; Facilities Officers
/// read and cancel all, and restart planning (retry-agent).
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

    /// <summary>
    /// Saves the request and starts planning it (§7.1 rule 6): 202 Accepted with the request, now AgentProcessing, and a
    /// Location to poll. The agent run continues in the background; the request moves to PendingApproval or AgentFailed.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Requesters)]
    [ProducesResponseType<BookingRequestDetailDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingRequestDetailDto>> Create(CreateBookingRequestRequest request, CancellationToken ct)
    {
        var created = await requests.CreateAsync(request, ct);
        return AcceptedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>
    /// Starts a new agent run (the next RevisionNo) for a request whose run failed, or a Submitted request that never got
    /// one. 202 with the request, now AgentProcessing; 409 for any other status or when the requester is at the cap.
    /// </summary>
    [HttpPost("{id:long}/retry-agent")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<BookingRequestDetailDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingRequestDetailDto>> RetryAgent(long id, CancellationToken ct)
        => await requests.RetryAgentAsync(id, ct) is { } detail ? AcceptedAtAction(nameof(Get), new { id }, detail) : NotFound();

    /// <summary>
    /// UC07: cancels the request. The owner may give a reason; a Facilities Officer must. Returns the updated request.
    /// An Approved request's booking is released and its quote voided; an owner's late cancel is flagged, not charged.
    /// </summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType<BookingRequestDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingRequestDetailDto>> Cancel(
        long id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelBookingRequestRequest? request, CancellationToken ct)
        => await requests.CancelAsync(id, request, ct) is { } detail ? Ok(detail) : NotFound();
}
