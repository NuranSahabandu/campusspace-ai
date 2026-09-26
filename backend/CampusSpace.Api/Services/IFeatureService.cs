using CampusSpace.Api.Dtos.Facilities;

namespace CampusSpace.Api.Services;

/// <summary>
/// Room features (reference data). Methods return null/false when the feature does not exist.
/// A duplicate code throws ConflictException (409). A feature used by a room cannot be deleted
/// or have its code changed (409 "In use"), because agents and equipment types refer to the code.
/// </summary>
public interface IFeatureService
{
    /// <summary>Sorted by code.</summary>
    Task<IReadOnlyList<FeatureDto>> ListAsync(CancellationToken ct = default);

    Task<FeatureDto?> GetAsync(long id, CancellationToken ct = default);
    Task<FeatureDto> CreateAsync(FeatureRequest request, CancellationToken ct = default);
    Task<FeatureDto?> UpdateAsync(long id, FeatureRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}
