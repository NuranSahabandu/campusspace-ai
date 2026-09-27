using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Data.Configurations;

namespace CampusSpace.Api.Dtos.Requests;

/// <summary>One portable equipment line. Each TypeId may appear once per request.</summary>
public record RequestedEquipmentLineRequest(
    [Range(1, long.MaxValue)] long TypeId,
    [Range(RequestedEquipmentLineConfiguration.MinQuantity, RequestedEquipmentLineConfiguration.MaxQuantity)] int Quantity);

/// <summary>
/// POST /api/booking-requests. Times may carry any offset (for example +05:30); they are stored as UTC and must be in
/// the future. BudgetLkr has at most two decimal places. RequiredFeatures are feature codes (case-insensitive,
/// duplicates ignored). A Student must name a club they represent; a Lecturer must not name one.
/// </summary>
public record CreateBookingRequestRequest(
    [Required, MaxLength(BookingRequestConfiguration.PurposeMaxLength)] string Purpose,
    [Range(BookingRequestConfiguration.MinAttendees, BookingRequestConfiguration.MaxAttendees)] int Attendees,
    [Required] DateTimeOffset? RequestedStart,
    [Required] DateTimeOffset? RequestedEnd,
    [Required, Range(typeof(decimal), "0", "99999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    decimal? BudgetLkr,
    string[]? RequiredFeatures,
    List<RequestedEquipmentLineRequest>? Equipment,
    long? ClubId,
    [MaxLength(BookingRequestConfiguration.NotesMaxLength)] string? Notes) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestedStart is { } start && RequestedEnd is { } end && end <= start)
            yield return new ValidationResult("End must be after Start.", [nameof(RequestedEnd)]);
    }
}

/// <summary>A request in a list. Times are UTC.</summary>
public record BookingRequestSummaryDto(
    long Id, string Purpose, string Status, DateTime RequestedStart, DateTime RequestedEnd, int Attendees,
    decimal BudgetLkr, string? ClubName, string RequesterName, DateTime CreatedAt);

public record RequesterDto(long Id, string Name, string Email);

public record ClubRefDto(long Id, string Name);

public record RequiredFeatureDto(string Code, string Name);

public record RequestedEquipmentDto(long TypeId, string TypeCode, string TypeName, int Quantity);

/// <summary>
/// One status change. ChangedById and ChangedByName are null when the system made the change. Clients compare
/// ChangedById with the requester's id to show "You" (names are not unique).
/// </summary>
public record RequestStatusHistoryDto(
    string? FromStatus, string ToStatus, long? ChangedById, string? ChangedByName, string? Reason, DateTime ChangedAt);

/// <summary>
/// A request with everything the requester entered, and its history oldest first. Times are UTC.
/// LatestProposal is always null until the agent workflow exists (Phase 3).
/// </summary>
public record BookingRequestDetailDto(
    long Id, string Purpose, string Status, int Attendees, DateTime RequestedStart, DateTime RequestedEnd,
    decimal BudgetLkr, string? Notes, RequesterDto Requester, ClubRefDto? Club,
    IReadOnlyList<RequiredFeatureDto> RequiredFeatures, IReadOnlyList<RequestedEquipmentDto> Equipment,
    IReadOnlyList<RequestStatusHistoryDto> History, object? LatestProposal, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>
/// GET /api/booking-requests/eligibility: what the new-request form needs. Clubs are the active clubs the caller
/// represents (always empty for a Lecturer). Reason explains why CanSubmit is false.
/// </summary>
public record EligibilityDto(
    bool CanSubmit, string? Reason, IReadOnlyList<ClubRefDto> Clubs, int OpenRequests, int MaxOpenRequests, bool ClubRequired);
