using CampusSpace.Api.Dtos.Equipment;

namespace CampusSpace.Api.Services;

/// <summary>
/// Equipment availability (§9 Component B business op; later the Equipment Allocation agent's tool and V08).
/// Equipment is held by EquipmentReservations of BookingStatuses.Active bookings. Every count is computed in SQL, with
/// the tstzrange && operator for overlap.
/// </summary>
public interface IEquipmentAvailabilityService
{
    /// <summary>
    /// Serviceable, reserved and available counts of one type for [start, end). Throws BusinessRuleException (400,
    /// keyed Start/End) when the slot breaks a V05 rule. Null when the type does not exist.
    /// </summary>
    Task<EquipmentAvailabilityDto?> GetAvailabilityAsync(long typeId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
}
