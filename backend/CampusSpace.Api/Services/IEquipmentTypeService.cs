using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Equipment;

namespace CampusSpace.Api.Services;

/// <summary>
/// Equipment types and their substitutes (§9 Component B). Methods return null/false when the type does not exist.
/// A duplicate code throws ConflictException (409). Codes are immutable (400 on Code). A type that still has items
/// cannot be deleted (409 "In use"); its substitute rows are removed with it.
/// </summary>
public interface IEquipmentTypeService
{
    Task<PagedResult<EquipmentTypeDto>> ListAsync(EquipmentTypesQuery query, CancellationToken ct = default);
    Task<EquipmentTypeDetailDto?> GetAsync(long id, CancellationToken ct = default);
    Task<EquipmentTypeDetailDto> CreateAsync(EquipmentTypeRequest request, CancellationToken ct = default);
    Task<EquipmentTypeDetailDto?> UpdateAsync(long id, EquipmentTypeRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>Ordered by code.</summary>
    Task<IReadOnlyList<EquipmentTypeRefDto>?> GetSubstitutesAsync(long id, CancellationToken ct = default);

    /// <summary>Replaces the whole set in one save. Self, unknown or repeated ids are a 400 on SubstituteTypeIds.</summary>
    Task<IReadOnlyList<EquipmentTypeRefDto>?> ReplaceSubstitutesAsync(long id, ReplaceSubstitutesRequest request, CancellationToken ct = default);
}
