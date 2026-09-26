using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;

namespace CampusSpace.Api.Services;

/// <summary>
/// Maintenance blackouts of one room. Methods return null/false when the room, or a blackout of that room, does not exist.
/// Flagging clashing approved bookings comes with Bookings (Phase 2).
/// </summary>
public interface IRoomBlackoutService
{
    Task<PagedResult<BlackoutDto>?> ListAsync(long roomId, BlackoutsQuery query, CancellationToken ct = default);
    Task<BlackoutDto?> GetAsync(long roomId, long blackoutId, CancellationToken ct = default);

    /// <summary>Stores [Start, End) in UTC, created by the current user.</summary>
    Task<BlackoutDto?> CreateAsync(long roomId, CreateBlackoutRequest request, CancellationToken ct = default);

    Task<bool> DeleteAsync(long roomId, long blackoutId, CancellationToken ct = default);
}
