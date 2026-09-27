using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Equipment;

namespace CampusSpace.Api.Services;

/// <summary>
/// Equipment items (§9 Component B). Methods return null when the item does not exist. There is no delete: items are
/// retired through Status. A duplicate asset tag throws ConflictException (409). Loans (Phase 2) own the OnLoan status,
/// so CRUD cannot set it and an item on loan accepts only a Notes change. An item's type cannot change.
/// </summary>
public interface IEquipmentItemService
{
    Task<PagedResult<EquipmentItemDto>> ListAsync(EquipmentItemsQuery query, CancellationToken ct = default);
    Task<EquipmentItemDto?> GetAsync(long id, CancellationToken ct = default);
    Task<EquipmentItemDto> CreateAsync(EquipmentItemRequest request, CancellationToken ct = default);
    Task<EquipmentItemDto?> UpdateAsync(long id, EquipmentItemRequest request, CancellationToken ct = default);
}
