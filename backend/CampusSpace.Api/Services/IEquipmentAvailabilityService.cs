using CampusSpace.Api.Dtos.Equipment;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

/// <summary>One equipment line to reserve for a booking. Quantity 0 (a room_builtin line, addendum B) reserves nothing.</summary>
public sealed record ReservationLine(long TypeId, int Quantity);

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

    /// <summary>
    /// The over-allocation guard, for the approval transaction (Phase 3). Runs inside the caller's READ COMMITTED
    /// transaction and never calls SaveChanges: the caller commits the booking, its reservations and the rest together.
    /// Locks each type, re-counts availability for the booking's TimeRange, and adds one EquipmentReservation per type
    /// (repeated types summed, qty 0 skipped, TimeRange copied from the booking). If any type is short, throws
    /// ConflictException (409) naming every short type and what is available, and adds nothing.
    /// </summary>
    Task ReserveAsync(Booking booking, IReadOnlyList<ReservationLine> lines, CancellationToken ct = default);
}
