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
