using CampusSpace.Api.Dtos.Facilities;

namespace CampusSpace.Api.Services;

/// <summary>
/// Room features (reference data). Methods return null/false when the feature does not exist.
/// A duplicate code throws ConflictException (409). Codes are immutable after creation, because agents and
/// equipment types refer to them: changing the code of a referenced feature is a 409 "In use", any other change
/// a 400 on Code. A referenced feature cannot be deleted (409 "In use").
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
