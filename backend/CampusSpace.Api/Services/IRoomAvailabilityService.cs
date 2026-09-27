using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;

namespace CampusSpace.Api.Services;

/// <summary>
/// What a room search needs (§9 Component A availability; later the Venue Matching agent's tool). Features are codes,
/// normalised like RoomService (trimmed, lower-case); a room must have ALL of them.
/// </summary>
public sealed record AvailabilityCriteria(
    DateTimeOffset Start,
    DateTimeOffset End,
    int MinCapacity,
    IReadOnlyList<string> Features,
    long? BuildingId = null,
    string? Type = null,
    int? MaxCapacity = null);

/// <summary>
/// Which rooms are free, and when (Component A). Busy means an overlapping RoomBlackout or an overlapping booking in
/// BookingStatuses.Active; overlap is computed in SQL with the tstzrange && operator.
/// </summary>
public interface IRoomAvailabilityService
{
    /// <summary>
    /// Active rooms that fit the criteria and are free for [Start, End), best fit first (capacity, then code).
    /// Throws BusinessRuleException (400) when the slot breaks a V05 rule (keyed Start/End) or a feature code is unknown.
    /// Lead time and the advance window (V06) are not checked: browsing a slot is not booking it.
    /// </summary>
    Task<PagedResult<RoomDto>> FindAvailableAsync(AvailabilityCriteria criteria, PageQuery page, CancellationToken ct = default);

    /// <summary>
    /// The room's busy and free time on campus date <paramref name="date"/> (UC03), with opening hours and slot
    /// boundaries from the current policy. Null if the room is missing, or inactive and the caller is not a
    /// Facilities Officer.
    /// </summary>
    Task<RoomScheduleDto?> GetScheduleAsync(long roomId, DateOnly date, CancellationToken ct = default);
}
