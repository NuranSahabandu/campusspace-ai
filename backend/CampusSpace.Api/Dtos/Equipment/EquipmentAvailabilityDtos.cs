using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Equipment;

/// <summary>
/// GET /api/equipment/availability: how many items of TypeId are free for [Start, End). Start and End are ISO 8601 with
/// an offset (encode "+" as %2B in a URL) and must follow the V05 slot rules, like room availability.
/// </summary>
public record EquipmentAvailabilityQuery : IValidatableObject
{
    [Required, Range(1, long.MaxValue)] public long? TypeId { get; init; }
    [Required] public DateTimeOffset? Start { get; init; }
    [Required] public DateTimeOffset? End { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Start is { } start && End is { } end && end <= start)
            yield return new ValidationResult("End must be after Start.", [nameof(End)]);
    }
}

/// <summary>
/// Serviceable counts items that are Available or OnLoan (an OnLoan item comes back; its use is already one of the
/// reservations). Reserved sums the reservations of Active bookings overlapping the window. Available is
/// max(0, Serviceable - Reserved). OverAllocated means Reserved exceeds Serviceable, for example after an item went
/// UnderRepair once reservations were made.
/// </summary>
public record EquipmentAvailabilityDto(
    long TypeId, string Code, string Name, int Serviceable, int Reserved, int Available, bool OverAllocated);
