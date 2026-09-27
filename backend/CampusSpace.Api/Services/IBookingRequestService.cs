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

    /// <summary>Whether the caller can submit now, and the clubs they can submit for.</summary>
    Task<EligibilityDto> GetEligibilityAsync(CancellationToken ct = default);
}
