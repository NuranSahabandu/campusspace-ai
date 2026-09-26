using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Facilities;

public record BuildingRefDto(long Id, string Code, string Name);

public record FeatureRefDto(string Code, string Name);

/// <summary>A room with its building and features (features ordered by code).</summary>
public record RoomDto(
    long Id, string Code, string Name, string Type, int Capacity, bool IsActive,
    BuildingRefDto Building, IReadOnlyList<FeatureRefDto> Features);

/// <summary>
/// Code is trimmed. FeatureCodes are trimmed and lower-cased; each must exist in Features. The building must be active.
/// </summary>
public record CreateRoomRequest(
    [Required, MaxLength(20)] string Code,
    [Required, MaxLength(100)] string Name,
    [Required, ValidRoomType] string Type,
    [Range(1, int.MaxValue)] int Capacity,
    [Range(1, long.MaxValue)] long BuildingId,
    string[]? FeatureCodes);

/// <summary>Replaces the whole feature set. IsActive = true reactivates a deleted (deactivated) room.</summary>
public record UpdateRoomRequest(
    [Required, MaxLength(20)] string Code,
    [Required, MaxLength(100)] string Name,
    [Required, ValidRoomType] string Type,
    [Range(1, int.MaxValue)] int Capacity,
    [Range(1, long.MaxValue)] long BuildingId,
    string[]? FeatureCodes,
    [Required] bool? IsActive);
