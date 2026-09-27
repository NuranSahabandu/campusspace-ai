using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Equipment;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class EquipmentItemService(AppDbContext db) : IEquipmentItemService
{
    private const string OnLoanMessage = "Item is on loan; use check-in";

    public Task<PagedResult<EquipmentItemDto>> ListAsync(EquipmentItemsQuery query, CancellationToken ct = default)
    {
        var items = db.EquipmentItems.AsNoTracking();

        if (query.TypeId is { } typeId)
            items = items.Where(i => i.TypeId == typeId);
        if (query.Status is { } status)
            items = items.Where(i => i.Status == status);
        if (query.Condition is { } condition)
            items = items.Where(i => i.Condition == condition);
        if (!string.IsNullOrWhiteSpace(query.Search))
            items = items.Where(i => EF.Functions.ILike(i.AssetTag, query.Search.ToContainsPattern()));

        items = query.Sort switch
        {
            "-assetTag" => items.OrderByDescending(i => i.AssetTag).ThenBy(i => i.Id),
            "type" => items.OrderBy(i => i.Type.Code).ThenBy(i => i.AssetTag).ThenBy(i => i.Id),
            "-type" => items.OrderByDescending(i => i.Type.Code).ThenBy(i => i.AssetTag).ThenBy(i => i.Id),
            "status" => items.OrderBy(i => i.Status).ThenBy(i => i.AssetTag).ThenBy(i => i.Id),
            "-status" => items.OrderByDescending(i => i.Status).ThenBy(i => i.AssetTag).ThenBy(i => i.Id),
            "condition" => items.OrderBy(i => i.Condition).ThenBy(i => i.AssetTag).ThenBy(i => i.Id),
            "-condition" => items.OrderByDescending(i => i.Condition).ThenBy(i => i.AssetTag).ThenBy(i => i.Id),
            "updatedAt" => items.OrderBy(i => i.UpdatedAt).ThenBy(i => i.Id),
            "-updatedAt" => items.OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.Id),
            _ => items.OrderBy(i => i.AssetTag).ThenBy(i => i.Id),
        };

        return ToDtos(items).ToPagedResultAsync(query, ct);
    }

    public Task<EquipmentItemDto?> GetAsync(long id, CancellationToken ct = default) =>
        ToDtos(db.EquipmentItems.AsNoTracking().Where(i => i.Id == id)).SingleOrDefaultAsync(ct);

    public async Task<EquipmentItemDto> CreateAsync(EquipmentItemRequest request, CancellationToken ct = default)
    {
        var assetTag = NormalizeAssetTag(request.AssetTag);
        if (!await db.EquipmentTypes.AnyAsync(t => t.Id == request.TypeId, ct))
            throw new BusinessRuleException("TypeId", "Equipment type does not exist.");
        CheckStatusAndCondition(request.Status, request.Condition);
        await EnsureAssetTagIsFreeAsync(assetTag, exceptId: null, ct);

        var item = new EquipmentItem
        {
            AssetTag = assetTag,
            TypeId = request.TypeId,
            Condition = request.Condition,
            Status = request.Status,
            Notes = NormalizeNotes(request.Notes),
        };
        db.EquipmentItems.Add(item);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(item.Id, ct))!;
    }

    public async Task<EquipmentItemDto?> UpdateAsync(long id, EquipmentItemRequest request, CancellationToken ct = default)
    {
        var item = await db.EquipmentItems.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
            return null;

        var assetTag = NormalizeAssetTag(request.AssetTag);

        if (item.Status == EquipmentItemStatuses.OnLoan)
        {
            // Loans own an item while it is out; check-in (Phase 2) records its status and condition.
            if (request.Status != item.Status)
                throw new BusinessRuleException("Status", OnLoanMessage);
            if (request.Condition != item.Condition)
                throw new BusinessRuleException("Condition", OnLoanMessage);
            if (request.TypeId != item.TypeId)
                throw new BusinessRuleException("TypeId", OnLoanMessage);
            if (assetTag != item.AssetTag)
                throw new BusinessRuleException("AssetTag", OnLoanMessage);
        }
        else
        {
            // Reservations and loans (Phase 2) count items per type.
            if (request.TypeId != item.TypeId)
                throw new BusinessRuleException("TypeId", "An item's type can't be changed");
            CheckStatusAndCondition(request.Status, request.Condition);
            if (assetTag != item.AssetTag)
                await EnsureAssetTagIsFreeAsync(assetTag, exceptId: id, ct);
        }

        item.AssetTag = assetTag;
        item.Condition = request.Condition;
        item.Status = request.Status;
        item.Notes = NormalizeNotes(request.Notes);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static IQueryable<EquipmentItemDto> ToDtos(IQueryable<EquipmentItem> items) => items.Select(i => new EquipmentItemDto(
        i.Id, i.AssetTag, i.TypeId, i.Type.Code, i.Type.Name, i.Condition, i.Status, i.Notes, i.UpdatedAt));

    /// <summary>OnLoan belongs to loans. A damaged item cannot be available.</summary>
    private static void CheckStatusAndCondition(string status, string condition)
    {
        if (status == EquipmentItemStatuses.OnLoan)
            throw new BusinessRuleException("Status", "Only loans can set OnLoan.");
        if (condition == EquipmentConditions.Damaged
            && status is not (EquipmentItemStatuses.UnderRepair or EquipmentItemStatuses.Retired))
            throw new BusinessRuleException("Condition", "Damaged items must be UnderRepair or Retired.");
    }

    private static string NormalizeAssetTag(string assetTag)
    {
        var normalized = assetTag.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            throw new BusinessRuleException("AssetTag", "Asset tag is required.");
        return normalized;
    }

    private static string? NormalizeNotes(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    /// <summary>Tags are stored upper-case, so an exact match is case-insensitive. The unique index still catches races (409).</summary>
    private async Task EnsureAssetTagIsFreeAsync(string assetTag, long? exceptId, CancellationToken ct)
    {
        if (await db.EquipmentItems.AnyAsync(i => i.AssetTag == assetTag && i.Id != exceptId, ct))
            throw new ConflictException("Asset tag is already taken");
    }
}
