using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Requests;

/// <summary>
/// GET /api/booking-requests. Status is repeatable and each value may be a comma list (?status=A,B&amp;status=C).
/// From/To are campus dates (yyyy-MM-dd), inclusive, on RequestedStart. Search matches the purpose, and for officers
/// also the requester's name or email and the club name. Newest first by default.
/// </summary>
public record BookingRequestsQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["createdAt", "requestedStart", "status", "attendees"];

    public string[]? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    [Range(1, long.MaxValue)] public long? ClubId { get; init; }

    /// <summary>The Status values split on commas, trimmed and de-duplicated.</summary>
    public IReadOnlyList<string> Statuses() => (Status ?? [])
        .SelectMany(s => s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .Distinct()
        .ToList();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var unknown = Statuses().Except(RequestStatuses.All).ToList();
        if (unknown.Count > 0)
            yield return new ValidationResult(
                $"Unknown status: {string.Join(", ", unknown)}. Use one of: {string.Join(", ", RequestStatuses.All)}.", [nameof(Status)]);
        if (From is { } from && To is { } to && from > to)
            yield return new ValidationResult("From must not be later than To.", [nameof(From)]);
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}
