using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Facilities;

/// <summary>
/// GET /api/rooms/availability: active rooms free for [Start, End) with Capacity in [MinCapacity, MaxCapacity] and ALL
/// the Features (comma-separated codes). Start and End are ISO 8601 with an offset (encode "+" as %2B in a URL).
/// Search matches code or name. The order is fixed (best fit: capacity, then code), so Sort is not accepted.
/// </summary>
public record RoomAvailabilityQuery : PageQuery, IValidatableObject
{
    [Required] public DateTimeOffset? Start { get; init; }
    [Required] public DateTimeOffset? End { get; init; }
    [Required, Range(1, int.MaxValue)] public int? MinCapacity { get; init; }
    [Range(1, int.MaxValue)] public int? MaxCapacity { get; init; }
    [MaxLength(500)] public string? Features { get; init; }
    [Range(1, long.MaxValue)] public long? BuildingId { get; init; }
    [ValidRoomType] public string? Type { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Start is { } start && End is { } end && end <= start)
            yield return new ValidationResult("End must be after Start.", [nameof(End)]);
        if (MinCapacity is { } min && MaxCapacity is { } max && max < min)
            yield return new ValidationResult("MaxCapacity can't be less than MinCapacity.", [nameof(MaxCapacity)]);
        if (Sort is not null)
            yield return new ValidationResult("Rooms are ordered by best fit (capacity, then code); Sort is not supported.", [nameof(Sort)]);
    }
}

/// <summary>The kinds of busy interval in a room's day schedule.</summary>
public static class ScheduleKinds
{
    public const string Booking = "Booking";
    public const string Blackout = "Blackout";

    /// <summary>The only label a booking shows: never the requester or the purpose (privacy).</summary>
    public const string BookedLabel = "Booked";
}

/// <summary>[Start, End) in UTC. Kind is a <see cref="ScheduleKinds"/> value; Label is "Booked" or the blackout's reason.</summary>
public record BusyIntervalDto(DateTime Start, DateTime End, string Kind, string Label);

/// <summary>[Start, End) in UTC, on slot boundaries.</summary>
public record FreeIntervalDto(DateTime Start, DateTime End);

/// <summary>
/// GET /api/rooms/{id}/schedule (UC03): one campus day. Open/Close are the day's opening hours ("HH:mm" campus time,
/// null when closed). Busy is every active booking and blackout overlapping the day, clipped to it. Free is the open
/// time not busy, as maximal intervals snapped inward to GranularityMinutes.
/// </summary>
public record RoomScheduleDto(
    DateOnly Date, string? Open, string? Close, int GranularityMinutes,
    IReadOnlyList<BusyIntervalDto> Busy, IReadOnlyList<FreeIntervalDto> Free);
