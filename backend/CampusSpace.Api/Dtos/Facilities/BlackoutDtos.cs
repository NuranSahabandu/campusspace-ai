using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Facilities;

/// <summary>
/// A blackout as [Start, End): Start is included, End is not. Both are UTC. ClashCount is the number of Active bookings it
/// clashes with now (the same rule as GET .../clashes).
/// </summary>
public record BlackoutDto(
    long Id, long RoomId, DateTime Start, DateTime End, string Reason, long CreatedById, string CreatedByName, DateTime CreatedAt,
    int ClashCount);

/// <summary>
/// An Active booking of the blackout's room whose [Start, End) overlaps it (UC14). Officer-only data, so it names the
/// requester. Start and End are UTC.
/// </summary>
public record BlackoutClashDto(
    long BookingId, long RequestId, DateTime Start, DateTime End, string Status, string RequesterName, string RequesterEmail);

/// <summary>
/// The create response: the blackout plus the bookings it clashes with. A blackout never cancels bookings; the officer
/// handles each clash.
/// </summary>
public record BlackoutWithClashesDto(
    long Id, long RoomId, DateTime Start, DateTime End, string Reason, long CreatedById, string CreatedByName, DateTime CreatedAt,
    int ClashCount, IReadOnlyList<BlackoutClashDto> Clashes)
    : BlackoutDto(Id, RoomId, Start, End, Reason, CreatedById, CreatedByName, CreatedAt, ClashCount);

/// <summary>Times may carry any offset (for example +05:30); they are stored as UTC.</summary>
public record CreateBlackoutRequest(
    [Required] DateTimeOffset? Start,
    [Required] DateTimeOffset? End,
    [Required, MaxLength(200)] string Reason) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Start is { } start && End is { } end && end <= start)
            yield return new ValidationResult("End must be after Start.", [nameof(End)]);
    }
}

/// <summary>GET /api/rooms/{id}/blackouts. Returns blackouts that overlap [From, To); either bound may be omitted.</summary>
public record BlackoutsQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["start"];

    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is { } from && To is { } to && to <= from)
            yield return new ValidationResult("To must be after From.", [nameof(To)]);
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}
