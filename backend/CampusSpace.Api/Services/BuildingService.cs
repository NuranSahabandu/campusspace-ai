using System.Text.RegularExpressions;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class BuildingService(AppDbContext db, ICurrentUser currentUser) : IBuildingService
{
    private bool IsOfficer => currentUser.IsInRole(Roles.FacilitiesOfficer);

    public async Task<IReadOnlyList<BuildingDto>> ListAsync(CancellationToken ct = default) =>
        await db.Buildings.AsNoTracking()
            .Where(b => b.IsActive || IsOfficer)
            .OrderBy(b => b.Code)
            .Select(b => new BuildingDto(b.Id, b.Code, b.Name, b.IsActive))
            .ToListAsync(ct);

    public Task<BuildingDto?> GetAsync(long id, CancellationToken ct = default) =>
        db.Buildings.AsNoTracking()
            .Where(b => b.Id == id && (b.IsActive || IsOfficer))
            .Select(b => new BuildingDto(b.Id, b.Code, b.Name, b.IsActive))
            .SingleOrDefaultAsync(ct);

    public async Task<BuildingDto> CreateAsync(CreateBuildingRequest request, CancellationToken ct = default)
    {
        var code = NormalizeCode(request.Code);
        await EnsureCodeIsFreeAsync(code, exceptId: null, ct);

        var building = new Building { Code = code, Name = request.Name.Trim() };
        db.Buildings.Add(building);
        await db.SaveChangesAsync(ct);
        return ToDto(building);
    }

    public async Task<BuildingDto?> UpdateAsync(long id, UpdateBuildingRequest request, CancellationToken ct = default)
    {
        var building = await db.Buildings.SingleOrDefaultAsync(b => b.Id == id, ct);
        if (building is null)
            return null;

        var code = NormalizeCode(request.Code);
        await EnsureCodeIsFreeAsync(code, exceptId: id, ct);

        building.Code = code;
        building.Name = request.Name.Trim();
        building.IsActive = request.IsActive!.Value;
        await db.SaveChangesAsync(ct);
        return ToDto(building);
    }

    /// <summary>A building that still has rooms fails with 23503, which GlobalExceptionHandler maps to 409 "In use".</summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var building = await db.Buildings.SingleOrDefaultAsync(b => b.Id == id, ct);
        if (building is null)
            return false;

        db.Buildings.Remove(building);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static BuildingDto ToDto(Building b) => new(b.Id, b.Code, b.Name, b.IsActive);

    private static string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(normalized, BuildingConfiguration.CodePattern))
            throw new BusinessRuleException("Code", "Code may contain only letters, digits and '-'.");
        return normalized;
    }

    private async Task EnsureCodeIsFreeAsync(string code, long? exceptId, CancellationToken ct)
    {
        if (await db.Buildings.AnyAsync(b => b.Code == code && b.Id != exceptId, ct))
            throw new ConflictException("Building code is already taken");
    }
}
