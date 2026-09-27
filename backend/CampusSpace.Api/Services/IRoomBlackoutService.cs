using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;

namespace CampusSpace.Api.Services;

/// <summary>
/// Maintenance blackouts of one room. Methods return null/false when the room, or a blackout of that room, does not exist.
/// A blackout never cancels bookings: it reports its clashes (Active bookings of the room overlapping it) for the officer.
/// </summary>
public interface IRoomBlackoutService
{
    Task<PagedResult<BlackoutDto>?> ListAsync(long roomId, BlackoutsQuery query, CancellationToken ct = default);
    Task<BlackoutDto?> GetAsync(long roomId, long blackoutId, CancellationToken ct = default);

    /// <summary>Stores [Start, End) in UTC, created by the current user, and returns it with its clashes.</summary>
    Task<BlackoutWithClashesDto?> CreateAsync(long roomId, CreateBlackoutRequest request, CancellationToken ct = default);

    /// <summary>The Active bookings of the room overlapping the blackout, by start time.</summary>
    Task<IReadOnlyList<BlackoutClashDto>?> GetClashesAsync(long roomId, long blackoutId, CancellationToken ct = default);

    Task<bool> DeleteAsync(long roomId, long blackoutId, CancellationToken ct = default);
}
