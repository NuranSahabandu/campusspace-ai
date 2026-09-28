using CampusSpace.Api.Dtos.AgentTools;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Requests;

namespace CampusSpace.Api.Services;

/// <summary>
/// Booking requests (§9 Component C) for the current caller. Requesters see only their own requests; Facilities
/// Officers see all. Reading someone else's request throws ForbiddenException (403); an unknown id returns null (404).
/// </summary>
public interface IBookingRequestService
{
    /// <summary>
    /// Validates the request and the caller's eligibility (V11), then saves it as Submitted with its equipment lines
    /// and first history row. Field problems are one 400 (BusinessRuleException); the open-request cap is a 409.
    /// </summary>
    Task<BookingRequestDetailDto> CreateAsync(CreateBookingRequestRequest request, CancellationToken ct = default);

    Task<PagedResult<BookingRequestSummaryDto>> ListAsync(BookingRequestsQuery query, CancellationToken ct = default);
    Task<BookingRequestDetailDto?> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<RequestStatusHistoryDto>?> GetHistoryAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// The object-level read rule (§15.1), shared by request detail, history and quotations: false when the request
    /// doesn't exist; throws ForbiddenException when the caller is a requester who doesn't own it.
    /// </summary>
    Task<bool> EnsureCanReadAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// UC07: cancels the request for its owner (reason optional) or a Facilities Officer (reason required, 400). Null
    /// when it doesn't exist (404); another requester's request is a ForbiddenException (403). A status the state
    /// machine can't cancel, or an Approved booking that has started, is a ConflictException (409). An Approved
    /// request's booking is cancelled and its live quote voided in the same transaction. An owner cancelling an Approved
    /// booking later than free_cancellation_hours before its start is flagged late (not charged).
    /// </summary>
    Task<BookingRequestDetailDto?> CancelAsync(long id, CancelBookingRequestRequest? request, CancellationToken ct = default);

    /// <summary>
    /// Officer only: starts a new agent run for a request whose run failed (AgentFailed), or for a Submitted request that
    /// has no live run (seeded or legacy data). The new run gets the next RevisionNo; older runs keep their status. The
    /// requester's open-request cap is re-checked under the submit lock. Null when the request doesn't exist (404); any
    /// other status, or a cap that would be exceeded, is a ConflictException (409). The start itself is best-effort.
    /// </summary>
    Task<BookingRequestDetailDto?> RetryAgentAsync(long id, CancellationToken ct = default);

    /// <summary>Whether the caller can submit now, and the clubs they can submit for.</summary>
    Task<EligibilityDto> GetEligibilityAsync(CancellationToken ct = default);

    /// <summary>
    /// The agent Supervisor's view of a request (GET /internal/agent-tools/request-context/{id}), with the same club and
    /// open-request rules as submission (V11). No caller check: only the AgentTools policy reaches it. Null if unknown.
    /// </summary>
    Task<AgentRequestContextDto?> GetAgentContextAsync(long id, CancellationToken ct = default);
}
