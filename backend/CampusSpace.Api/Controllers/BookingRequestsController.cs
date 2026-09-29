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
/// read and cancel all, restart planning (retry-agent), and decide proposals (approve, reject, request-revision).
/// Stacked [Authorize] attributes must all pass, so submit and eligibility are for requesters only.
/// </summary>
[ApiController]
[Route("api/booking-requests")]
[Authorize(Roles = $"{Roles.Student},{Roles.Lecturer},{Roles.FacilitiesOfficer}")]
public class BookingRequestsController(IBookingRequestService requests, IApprovalService approvals) : ControllerBase
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
    /// UC19: approves the proposal. 200 with the request, now Approved (Booking, reservations and Issued quote created in one
    /// transaction). 202 ApprovalInProgress when the agent hasn't confirmed within AgentService:ApprovalWaitSeconds; the
    /// poller finishes it. 409 when the request isn't PendingApproval, a decision is already being made, or the final check
    /// failed: the title says whether the request was closed (the time is no longer valid) or a new proposal is being prepared.
    /// </summary>
    [HttpPost("{id:long}/approve")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<BookingRequestDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApprovalInProgressDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        long id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveRequest? request, CancellationToken ct)
    {
        var result = await approvals.ApproveAsync(id, request?.Comment, ct);
        if (result is null)
            return NotFound();
        return result.InProgress
            ? AcceptedAtAction(nameof(Get), new { id }, new ApprovalInProgressDto(id, ApprovalInProgressDto.InProgress))
            : Ok(result.Detail);
    }

    /// <summary>UC20: rejects the proposal with a reason (required). 200 with the request, now Rejected.</summary>
    [HttpPost("{id:long}/reject")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<BookingRequestDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingRequestDetailDto>> Reject(long id, RejectRequest request, CancellationToken ct)
        => await approvals.RejectAsync(id, request.Reason, ct) is { } detail ? Ok(detail) : NotFound();

    /// <summary>
    /// UC21: asks the agents for a new proposal, with the officer's notes (required). 202 with the request, now
    /// AgentProcessing; it returns to PendingApproval when the new proposal is ready.
    /// </summary>
    [HttpPost("{id:long}/request-revision")]
    [Authorize(Roles = Roles.FacilitiesOfficer)]
    [ProducesResponseType<BookingRequestDetailDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingRequestDetailDto>> RequestRevision(long id, RevisionRequest request, CancellationToken ct)
        => await approvals.RequestRevisionAsync(id, request.Notes, ct) is { } detail
            ? AcceptedAtAction(nameof(Get), new { id }, detail)
            : NotFound();

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
