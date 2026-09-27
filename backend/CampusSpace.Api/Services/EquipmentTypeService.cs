using System.Text.RegularExpressions;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Equipment;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class EquipmentTypeService(AppDbContext db) : IEquipmentTypeService
{
    public Task<PagedResult<EquipmentTypeDto>> ListAsync(EquipmentTypesQuery query, CancellationToken ct = default)
    {
        var types = db.EquipmentTypes.AsNoTracking();

        if (query.Category is { } category)
            types = types.Where(t => t.Category == category);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = query.Search.ToContainsPattern();
            types = types.Where(t => EF.Functions.ILike(t.Code, pattern) || EF.Functions.ILike(t.Name, pattern));
        }

        types = query.Sort switch
        {
            "-code" => types.OrderByDescending(t => t.Code).ThenBy(t => t.Id),
            "name" => types.OrderBy(t => t.Name).ThenBy(t => t.Id),
            "-name" => types.OrderByDescending(t => t.Name).ThenBy(t => t.Id),
            "category" => types.OrderBy(t => t.Category).ThenBy(t => t.Code).ThenBy(t => t.Id),
            "-category" => types.OrderByDescending(t => t.Category).ThenBy(t => t.Code).ThenBy(t => t.Id),
            "fee" => types.OrderBy(t => t.FeePerBooking).ThenBy(t => t.Code).ThenBy(t => t.Id),
            "-fee" => types.OrderByDescending(t => t.FeePerBooking).ThenBy(t => t.Code).ThenBy(t => t.Id),
            _ => types.OrderBy(t => t.Code).ThenBy(t => t.Id),
        };

        return ToDtos(types).ToPagedResultAsync(query, ct);
    }

    public async Task<EquipmentTypeDetailDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var row = await ToDtos(db.EquipmentTypes.AsNoTracking().Where(t => t.Id == id)).SingleOrDefaultAsync(ct);
        if (row is null)
            return null;

        var substitutes = await SubstitutesOf(id).ToListAsync(ct);
        return new EquipmentTypeDetailDto(
            row.Id, row.Code, row.Name, row.Category, row.FeePerBooking,
            row.CoveredByFeatureCode, row.CoveredByFeatureName, row.ItemCounts, substitutes);
    }

    public async Task<EquipmentTypeDetailDto> CreateAsync(EquipmentTypeRequest request, CancellationToken ct = default)
    {
        var code = NormalizeCode(request.Code);
        var coveredBy = await ResolveCoveredByAsync(request.CoveredByFeatureCode, ct);
        var fee = CheckFee(request.FeePerBooking!.Value);
        await EnsureCodeIsFreeAsync(code, ct);

        var type = new EquipmentType
        {
            Code = code,
            Name = request.Name.Trim(),
            Category = request.Category,
            FeePerBooking = fee,
            CoveredByFeatureCode = coveredBy,
        };
        db.EquipmentTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(type.Id, ct))!;
    }

    public async Task<EquipmentTypeDetailDto?> UpdateAsync(long id, EquipmentTypeRequest request, CancellationToken ct = default)
    {
        var type = await db.EquipmentTypes.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (type is null)
            return null;

        // Immutable: requests, proposals and the agents refer to the code.
        if (NormalizeCode(request.Code) != type.Code)
            throw new BusinessRuleException("Code", "Codes can't be changed");
        var coveredBy = await ResolveCoveredByAsync(request.CoveredByFeatureCode, ct);

        type.Name = request.Name.Trim();
        type.Category = request.Category;
        type.FeePerBooking = CheckFee(request.FeePerBooking!.Value);
        type.CoveredByFeatureCode = coveredBy;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>A type that still has items fails with 23503, which GlobalExceptionHandler maps to 409 "In use".</summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var type = await db.EquipmentTypes.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (type is null)
            return false;

        db.EquipmentTypes.Remove(type);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<EquipmentTypeRefDto>?> GetSubstitutesAsync(long id, CancellationToken ct = default)
    {
        if (!await db.EquipmentTypes.AnyAsync(t => t.Id == id, ct))
            return null;

        return await SubstitutesOf(id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EquipmentTypeRefDto>?> ReplaceSubstitutesAsync(
        long id, ReplaceSubstitutesRequest request, CancellationToken ct = default)
    {
        var type = await db.EquipmentTypes.Include(t => t.Substitutes).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (type is null)
            return null;

        const string field = nameof(ReplaceSubstitutesRequest.SubstituteTypeIds);
        var ids = request.SubstituteTypeIds;
        if (ids.Contains(id))
            throw new BusinessRuleException(field, "A type cannot be its own substitute.");
        var repeated = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repeated.Count > 0)
            throw new BusinessRuleException(field, $"Repeated ids: {string.Join(", ", repeated)}.");
        var known = await db.EquipmentTypes.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
        var unknown = ids.Except(known).ToList();
        if (unknown.Count > 0)
            throw new BusinessRuleException(field, $"Unknown equipment type ids: {string.Join(", ", unknown)}.");

        // Diff rather than clear-and-re-add: re-adding a removed key in one save confuses the change tracker.
        var wanted = ids.ToHashSet();
        type.Substitutes.RemoveAll(s => !wanted.Contains(s.SubstituteTypeId));
        var have = type.Substitutes.Select(s => s.SubstituteTypeId).ToHashSet();
        type.Substitutes.AddRange(wanted.Where(sid => !have.Contains(sid)).Select(sid => new EquipmentSubstitute { SubstituteTypeId = sid }));

        await db.SaveChangesAsync(ct);
        return await GetSubstitutesAsync(id, ct);
    }

    private static IQueryable<EquipmentTypeDto> ToDtos(IQueryable<EquipmentType> types) => types.Select(t => new EquipmentTypeDto(
        t.Id, t.Code, t.Name, t.Category, t.FeePerBooking,
        t.CoveredByFeatureCode, t.CoveredByFeature != null ? t.CoveredByFeature.Name : null,
        new EquipmentItemCountsDto(
            t.Items.Count,
            t.Items.Count(i => i.Status == EquipmentItemStatuses.Available),
            t.Items.Count(i => i.Status == EquipmentItemStatuses.OnLoan),
            t.Items.Count(i => i.Status == EquipmentItemStatuses.UnderRepair),
            t.Items.Count(i => i.Status == EquipmentItemStatuses.Retired))));

    private IQueryable<EquipmentTypeRefDto> SubstitutesOf(long id) => db.EquipmentSubstitutes.AsNoTracking()
        .Where(s => s.TypeId == id)
        .OrderBy(s => s.SubstituteType.Code)
        .Select(s => new EquipmentTypeRefDto(s.SubstituteType.Id, s.SubstituteType.Code, s.SubstituteType.Name));

    private static string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length < EquipmentTypeConfiguration.CodeMinLength || !Regex.IsMatch(normalized, EquipmentTypeConfiguration.CodePattern))
            throw new BusinessRuleException("Code", "Code must be 2-40 upper-case letters and digits in hyphen-separated parts, for example MIC-WIRELESS.");
        return normalized;
    }

    /// <summary>numeric(10,2) would silently round a third decimal place, so reject it instead.</summary>
    private static decimal CheckFee(decimal fee)
    {
        if (decimal.Round(fee, 2) != fee)
            throw new BusinessRuleException("FeePerBooking", "Fee can have at most 2 decimal places.");
        return fee;
    }

    /// <summary>Null or blank means none. An unknown code is a 400 here, not a 23503 "In use" from the database.</summary>
    private async Task<string?> ResolveCoveredByAsync(string? featureCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(featureCode))
            return null;

        var code = featureCode.Trim().ToLowerInvariant();
        if (!await db.Features.AnyAsync(f => f.Code == code, ct))
            throw new BusinessRuleException("CoveredByFeatureCode", $"Unknown feature code: {code}.");
        return code;
    }

    /// <summary>Codes are stored upper-case, so an exact match is case-insensitive. The unique index still catches races (409).</summary>
    private async Task EnsureCodeIsFreeAsync(string code, CancellationToken ct)
    {
        if (await db.EquipmentTypes.AnyAsync(t => t.Code == code, ct))
            throw new ConflictException("Equipment type code is already taken");
    }
}
