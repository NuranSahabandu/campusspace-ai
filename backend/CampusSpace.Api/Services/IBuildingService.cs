using CampusSpace.Api.Dtos.Facilities;

namespace CampusSpace.Api.Services;

/// <summary>
/// Buildings (reference data). Methods return null/false when the building does not exist.
/// A duplicate code throws ConflictException (409); deleting a building that still has rooms is a 409 "In use".
/// </summary>
public interface IBuildingService
{
    /// <summary>Sorted by code. Inactive buildings are included for Facilities Officers only.</summary>
    Task<IReadOnlyList<BuildingDto>> ListAsync(CancellationToken ct = default);

    Task<BuildingDto?> GetAsync(long id, CancellationToken ct = default);
    Task<BuildingDto> CreateAsync(CreateBuildingRequest request, CancellationToken ct = default);
    Task<BuildingDto?> UpdateAsync(long id, UpdateBuildingRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}
