using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;

namespace CampusSpace.Api.Services;

/// <summary>
/// Rooms and their features. Methods return null/false when the room does not exist (or, for non-officers, is inactive).
/// Rule breaks throw BusinessRuleException (400) or ConflictException (409).
/// </summary>
public interface IRoomService
{
    Task<PagedResult<RoomDto>> ListAsync(RoomsQuery query, CancellationToken ct = default);

    /// <summary>Null if missing, or inactive and the caller is not a Facilities Officer.</summary>
    Task<RoomDto?> GetAsync(long id, CancellationToken ct = default);

    Task<RoomDto> CreateAsync(CreateRoomRequest request, CancellationToken ct = default);

    /// <summary>Replaces the feature set in the same save as the other fields.</summary>
    Task<RoomDto?> UpdateAsync(long id, UpdateRoomRequest request, CancellationToken ct = default);

    /// <summary>Soft delete: sets IsActive = false.</summary>
    Task<bool> DeactivateAsync(long id, CancellationToken ct = default);
}
