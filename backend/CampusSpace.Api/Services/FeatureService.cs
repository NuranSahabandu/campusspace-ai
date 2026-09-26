using System.Text.RegularExpressions;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class FeatureService(AppDbContext db) : IFeatureService
{
    public async Task<IReadOnlyList<FeatureDto>> ListAsync(CancellationToken ct = default) =>
        await db.Features.AsNoTracking()
            .OrderBy(f => f.Code)
            .Select(f => new FeatureDto(f.Id, f.Code, f.Name))
            .ToListAsync(ct);

    public Task<FeatureDto?> GetAsync(long id, CancellationToken ct = default) =>
        db.Features.AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new FeatureDto(f.Id, f.Code, f.Name))
            .SingleOrDefaultAsync(ct);

    public async Task<FeatureDto> CreateAsync(FeatureRequest request, CancellationToken ct = default)
    {
        var code = NormalizeCode(request.Code);
        await EnsureCodeIsFreeAsync(code, exceptId: null, ct);

        var feature = new Feature { Code = code, Name = request.Name.Trim() };
        db.Features.Add(feature);
        await db.SaveChangesAsync(ct);
        return ToDto(feature);
    }

    public async Task<FeatureDto?> UpdateAsync(long id, FeatureRequest request, CancellationToken ct = default)
    {
        var feature = await db.Features.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
            return null;

        var code = NormalizeCode(request.Code);
        if (code != feature.Code)
        {
            await EnsureCodeIsFreeAsync(code, exceptId: id, ct);
            // Block rather than cascade: agents and proposals refer to the code (addendum B.2).
            if (await db.RoomFeatures.AnyAsync(rf => rf.FeatureId == id, ct))
                throw new ConflictException("In use");
        }

        feature.Code = code;
        feature.Name = request.Name.Trim();
        await db.SaveChangesAsync(ct);
        return ToDto(feature);
    }

    /// <summary>A feature that a room still has fails with 23503, which GlobalExceptionHandler maps to 409 "In use".</summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var feature = await db.Features.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
            return false;

        db.Features.Remove(feature);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static FeatureDto ToDto(Feature f) => new(f.Id, f.Code, f.Name);

    private static string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(normalized, FeatureConfiguration.CodePattern))
            throw new BusinessRuleException("Code", "Code must be snake_case: letters, digits and single underscores, starting with a letter.");
        return normalized;
    }

    private async Task EnsureCodeIsFreeAsync(string code, long? exceptId, CancellationToken ct)
    {
        if (await db.Features.AnyAsync(f => f.Code == code && f.Id != exceptId, ct))
            throw new ConflictException("Feature code is already taken");
    }
}
