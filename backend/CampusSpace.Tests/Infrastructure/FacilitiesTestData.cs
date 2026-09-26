using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Facilities rows for endpoint tests. The shared test database is migrated but not seeded, so tests add the
/// seed feature codes themselves (idempotent) and put their rooms in a building of their own.
/// </summary>
public static class FacilitiesTestData
{
    public static readonly IReadOnlyList<string> FeatureCodes =
        ["projector", "computers", "whiteboard", "ac", "sound_system", "smart_board"];

    /// <summary>A short unique prefix: building codes are at most 10 characters and room codes at most 20.</summary>
    public static string UniquePrefix() => "T" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    public static async Task EnsureFeaturesAsync(CustomWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Features.Select(f => f.Code).ToListAsync();
        db.Features.AddRange(FeatureCodes.Except(existing).Select(code => new Feature { Code = code, Name = code }));
        await db.SaveChangesAsync();
    }

    public static async Task<long> CreateBuildingAsync(CustomWebApplicationFactory factory, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var building = new Building { Code = code, Name = $"Building {code}" };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();
        return building.Id;
    }

    public static async Task<long> CreateRoomAsync(
        CustomWebApplicationFactory factory, long buildingId, string code, string type, int capacity,
        IEnumerable<string> featureCodes, bool isActive = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var codes = featureCodes.ToList();
        var features = await db.Features.Where(f => codes.Contains(f.Code)).ToListAsync();
        var room = new Room
        {
            BuildingId = buildingId, Code = code, Name = $"Room {code}", Type = type, Capacity = capacity, IsActive = isActive,
            RoomFeatures = features.Select(f => new RoomFeature { Feature = f }).ToList(),
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room.Id;
    }
}
