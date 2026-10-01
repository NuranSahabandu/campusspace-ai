using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class RoomService(AppDbContext db, ICurrentUser currentUser) : IRoomService
{
    private bool IsOfficer => currentUser.IsInRole(Roles.FacilitiesOfficer);

    public Task<PagedResult<RoomDto>> ListAsync(RoomsQuery query, CancellationToken ct = default)
    {
        var rooms = db.Rooms.AsNoTracking();

        if (!(query.IncludeInactive && IsOfficer))
            rooms = rooms.Where(r => r.IsActive);
        if (query.BuildingId is { } buildingId)
            rooms = rooms.Where(r => r.BuildingId == buildingId);
        if (query.Type is { } type)
            rooms = rooms.Where(r => r.Type == type);
        if (query.MinCapacity is { } minCapacity)
            rooms = rooms.Where(r => r.Capacity >= minCapacity);

        var codes = query.FeatureCodes();
        if (codes.Count > 0)
            // Codes are distinct, so matching all of them means the count of matches equals the count of codes.
            rooms = rooms.Where(r => r.RoomFeatures.Count(rf => codes.Contains(rf.Feature.Code)) == codes.Count);

        rooms = rooms.WhereContains(query.Search, r => r.Code, r => r.Name);

        rooms = query.Sort switch
        {
            "-code" => rooms.OrderByDescending(r => r.Code).ThenBy(r => r.Id),
            "name" => rooms.OrderBy(r => r.Name).ThenBy(r => r.Id),
            "-name" => rooms.OrderByDescending(r => r.Name).ThenBy(r => r.Id),
            "capacity" => rooms.OrderBy(r => r.Capacity).ThenBy(r => r.Id),
            "-capacity" => rooms.OrderByDescending(r => r.Capacity).ThenBy(r => r.Id),
            "building" => rooms.OrderBy(r => r.Building.Code).ThenBy(r => r.Code).ThenBy(r => r.Id),
            "-building" => rooms.OrderByDescending(r => r.Building.Code).ThenBy(r => r.Code).ThenBy(r => r.Id),
            _ => rooms.OrderBy(r => r.Code).ThenBy(r => r.Id),
        };

        return ToDtos(rooms).ToPagedResultAsync(query, ct);
    }

    public Task<RoomDto?> GetAsync(long id, CancellationToken ct = default) =>
        ToDtos(db.Rooms.AsNoTracking().Where(r => r.Id == id && (r.IsActive || IsOfficer))).SingleOrDefaultAsync(ct);

    public async Task<RoomDto> CreateAsync(CreateRoomRequest request, CancellationToken ct = default)
    {
        var code = request.Code.Trim();
        await EnsureBuildingIsActiveAsync(request.BuildingId, ct);
        var features = await LoadFeaturesAsync(request.FeatureCodes, ct);
        await EnsureCodeIsFreeAsync(code, exceptId: null, ct);

        var room = new Room
        {
            Code = code,
            Name = request.Name.Trim(),
            Type = request.Type,
            Capacity = request.Capacity,
            BuildingId = request.BuildingId,
            RoomFeatures = features.Select(f => new RoomFeature { FeatureId = f.Id }).ToList(),
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(room.Id, ct))!;
    }

    public async Task<RoomDto?> UpdateAsync(long id, UpdateRoomRequest request, CancellationToken ct = default)
    {
        var room = await db.Rooms.Include(r => r.RoomFeatures).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (room is null)
            return null;

        var code = request.Code.Trim();
        if (request.BuildingId != room.BuildingId)
            await EnsureBuildingIsActiveAsync(request.BuildingId, ct);
        var features = await LoadFeaturesAsync(request.FeatureCodes, ct);
        await EnsureCodeIsFreeAsync(code, exceptId: id, ct);

        room.Code = code;
        room.Name = request.Name.Trim();
        room.Type = request.Type;
        room.Capacity = request.Capacity;
        room.BuildingId = request.BuildingId;
        room.IsActive = request.IsActive!.Value;

        // Diff rather than clear-and-re-add: re-adding a removed key in one save confuses the change tracker.
        var wanted = features.Select(f => f.Id).ToHashSet();
        room.RoomFeatures.RemoveAll(rf => !wanted.Contains(rf.FeatureId));
        var have = room.RoomFeatures.Select(rf => rf.FeatureId).ToHashSet();
        room.RoomFeatures.AddRange(wanted.Where(fid => !have.Contains(fid)).Select(fid => new RoomFeature { FeatureId = fid }));

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeactivateAsync(long id, CancellationToken ct = default)
    {
        var room = await db.Rooms.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (room is null)
            return false;

        room.IsActive = false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The room row of GET /api/rooms, also used by availability search.</summary>
    internal static IQueryable<RoomDto> ToDtos(IQueryable<Room> rooms) => rooms.Select(r => new RoomDto(
        r.Id, r.Code, r.Name, r.Type, r.Capacity, r.IsActive,
        new BuildingRefDto(r.Building.Id, r.Building.Code, r.Building.Name),
        r.RoomFeatures.OrderBy(rf => rf.Feature.Code).Select(rf => new FeatureRefDto(rf.Feature.Code, rf.Feature.Name)).ToList()));

    private async Task EnsureBuildingIsActiveAsync(long buildingId, CancellationToken ct)
    {
        if (!await db.Buildings.AnyAsync(b => b.Id == buildingId && b.IsActive, ct))
            throw new BusinessRuleException("BuildingId", "Building does not exist or is inactive.");
    }

    /// <summary>Unknown codes are a 400 on FeatureCodes, listing every one of them.</summary>
    private async Task<List<Feature>> LoadFeaturesAsync(string[]? featureCodes, CancellationToken ct)
    {
        var codes = (featureCodes ?? []).Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList();
        if (codes.Count == 0)
            return [];

        var features = await db.Features.Where(f => codes.Contains(f.Code)).ToListAsync(ct);
        var unknown = codes.Except(features.Select(f => f.Code)).ToList();
        if (unknown.Count > 0)
            throw new BusinessRuleException("FeatureCodes", $"Unknown feature codes: {string.Join(", ", unknown)}.");
        return features;
    }

    /// <summary>Case-insensitive, so "a301" cannot sit next to "A301". The unique index still catches races (409).</summary>
    private async Task EnsureCodeIsFreeAsync(string code, long? exceptId, CancellationToken ct)
    {
        var lower = code.ToLower();
        if (await db.Rooms.AnyAsync(r => r.Code.ToLower() == lower && r.Id != exceptId, ct))
            throw new ConflictException("Room code is already taken");
    }
}
