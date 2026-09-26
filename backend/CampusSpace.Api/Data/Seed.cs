using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace CampusSpace.Api.Data;

/// <summary>
/// Development seed data. Each step checks its own table (users by email, clubs by "is the table empty",
/// features, buildings and rooms by code, blackouts by "is the table empty"),
/// so startup can call this every time and it also fills in a database that already has some rows.
/// </summary>
public static class Seed
{
    /// <summary>Demo accounts. All share the public demo password (config key Seed:DemoPassword).</summary>
    public static readonly IReadOnlyList<(string Email, string FullName, string Role)> DemoUsers =
    [
        ("kavindi@campusspace.local", "Kavindi Perera", Roles.Student),
        ("lecturer@campusspace.local", "Dr. Nimal Fernando", Roles.Lecturer),
        ("tech@campusspace.local", "Sunil Jayasinghe", Roles.LabTechnician),
        ("perera@campusspace.local", "Mr. Perera", Roles.FacilitiesOfficer),
        ("admin@campusspace.local", "System Admin", Roles.Admin),
        ("ishan@campusspace.local", "Ishan Silva", Roles.Student),
        ("nethmi@campusspace.local", "Nethmi Rajapaksa", Roles.Student),
        ("tharindu@campusspace.local", "Tharindu Wickramasinghe", Roles.Student),
    ];

    /// <summary>Three clubs. The first member of each is its representative.</summary>
    public static readonly IReadOnlyList<(string Name, string[] MemberEmails)> DemoClubs =
    [
        ("Robotics Club", ["kavindi@campusspace.local", "ishan@campusspace.local", "lecturer@campusspace.local"]),
        ("Drama Society", ["nethmi@campusspace.local", "tharindu@campusspace.local"]),
        ("IEEE Student Branch", ["tharindu@campusspace.local", "ishan@campusspace.local", "kavindi@campusspace.local"]),
    ];

    /// <summary>Room features. The codes are what the agents use.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> DemoFeatures =
    [
        ("projector", "Projector"),
        ("computers", "Computers"),
        ("whiteboard", "Whiteboard"),
        ("ac", "Air conditioning"),
        ("sound_system", "Sound system"),
        ("smart_board", "Smart board"),
    ];

    public static readonly IReadOnlyList<(string Code, string Name)> DemoBuildings =
    [
        ("MB", "Main Building"),
        ("NB", "New Building"),
        ("EB", "Engineering Building"),
    ];

    /// <summary>
    /// Rooms covering every type. A301, A305 and N201 are the labs of the plan's walkthrough (§11, App. B):
    /// only A301 and N201 have both computers and a projector. E201 has both but seats 40, and E305 is inactive.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name, string Type, int Capacity, string Building, string[] Features, bool IsActive)> DemoRooms =
    [
        ("A301", "Computer Lab A301", RoomTypes.ComputerLab, 48, "MB", ["computers", "projector", "ac", "whiteboard"], true),
        ("A305", "Computer Lab A305", RoomTypes.ComputerLab, 50, "MB", ["computers", "whiteboard", "ac"], true),
        ("A101", "Lecture Hall A101", RoomTypes.LectureHall, 120, "MB", ["projector", "whiteboard", "ac", "sound_system"], true),
        ("A102", "Lecture Hall A102", RoomTypes.LectureHall, 80, "MB", ["projector", "whiteboard"], true),
        ("A201", "Seminar Room A201", RoomTypes.SeminarRoom, 20, "MB", ["whiteboard", "smart_board"], true),
        ("A202", "Seminar Room A202", RoomTypes.SeminarRoom, 25, "MB", ["whiteboard", "ac"], true),
        ("MB-AUD", "Main Auditorium", RoomTypes.Auditorium, 300, "MB", ["projector", "sound_system", "ac"], true),
        ("N201", "Computer Lab N201", RoomTypes.ComputerLab, 60, "NB", ["computers", "projector", "ac", "smart_board"], true),
        ("N101", "Lecture Hall N101", RoomTypes.LectureHall, 150, "NB", ["projector", "ac", "sound_system", "whiteboard"], true),
        ("N102", "Lecture Hall N102", RoomTypes.LectureHall, 100, "NB", ["projector", "whiteboard", "ac"], true),
        ("N301", "Seminar Room N301", RoomTypes.SeminarRoom, 30, "NB", ["smart_board", "ac"], true),
        ("N302", "Seminar Room N302", RoomTypes.SeminarRoom, 15, "NB", ["whiteboard"], true),
        ("E101", "Lecture Hall E101", RoomTypes.LectureHall, 90, "EB", ["projector", "whiteboard"], true),
        ("E201", "Computer Lab E201", RoomTypes.ComputerLab, 40, "EB", ["computers", "projector", "whiteboard"], true),
        ("EB-AUD", "Engineering Auditorium", RoomTypes.Auditorium, 220, "EB", ["sound_system", "projector"], true),
        ("E305", "Seminar Room E305", RoomTypes.SeminarRoom, 20, "EB", ["whiteboard"], false),
    ];

    public const string DemoBlackoutRoom = "A101";
    public const string DemoBlackoutReason = "Projector maintenance";

    /// <summary>Sri Lanka has no daylight saving, so campus time is always UTC+05:30.</summary>
    private static readonly TimeSpan CampusOffset = TimeSpan.FromMinutes(330);

    public static async Task SeedAsync(AppDbContext db, string demoPassword, CancellationToken ct = default)
    {
        await SeedUsersAsync(db, demoPassword, ct);
        await SeedClubsAsync(db, ct);
        await SeedFeaturesAsync(db, ct);
        await SeedBuildingsAsync(db, ct);
        await SeedRoomsAsync(db, ct);
        await SeedBlackoutsAsync(db, ct);
    }

    private static async Task SeedUsersAsync(AppDbContext db, string demoPassword, CancellationToken ct)
    {
        var emails = DemoUsers.Select(u => u.Email).ToList();
        var existing = await db.Users.Where(u => emails.Contains(u.Email)).Select(u => u.Email).ToListAsync(ct);
        var missing = DemoUsers.Where(u => !existing.Contains(u.Email)).ToList();
        if (missing.Count == 0)
            return;

        // Hash once: every demo account has the same password.
        var hash = BCrypt.Net.BCrypt.HashPassword(demoPassword);
        db.Users.AddRange(missing.Select(u => new User
        {
            Email = u.Email,
            FullName = u.FullName,
            Role = u.Role,
            PasswordHash = hash,
        }));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedClubsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Clubs.AnyAsync(ct))
            return;

        var emails = DemoClubs.SelectMany(c => c.MemberEmails).Distinct().ToList();
        var userIds = await db.Users.Where(u => emails.Contains(u.Email)).ToDictionaryAsync(u => u.Email, u => u.Id, ct);
        var joinedAt = DateTime.UtcNow;

        db.Clubs.AddRange(DemoClubs.Select(c => new Club
        {
            Name = c.Name,
            Members = c.MemberEmails.Select((email, i) => new ClubMember
            {
                UserId = userIds[email],
                IsRepresentative = i == 0,
                JoinedAt = joinedAt,
            }).ToList(),
        }));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedFeaturesAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.Features.Select(f => f.Code).ToListAsync(ct);
        db.Features.AddRange(DemoFeatures.Where(f => !existing.Contains(f.Code)).Select(f => new Feature { Code = f.Code, Name = f.Name }));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedBuildingsAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.Buildings.Select(b => b.Code).ToListAsync(ct);
        db.Buildings.AddRange(DemoBuildings.Where(b => !existing.Contains(b.Code)).Select(b => new Building { Code = b.Code, Name = b.Name }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds missing rooms with their features. Rooms that already exist are left as they are.</summary>
    private static async Task SeedRoomsAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.Rooms.Select(r => r.Code).ToListAsync(ct);
        var missing = DemoRooms.Where(r => !existing.Contains(r.Code)).ToList();
        if (missing.Count == 0)
            return;

        var buildingIds = await db.Buildings.ToDictionaryAsync(b => b.Code, b => b.Id, ct);
        var featureIds = await db.Features.ToDictionaryAsync(f => f.Code, f => f.Id, ct);
        db.Rooms.AddRange(missing.Select(r => new Room
        {
            Code = r.Code,
            Name = r.Name,
            Type = r.Type,
            Capacity = r.Capacity,
            BuildingId = buildingIds[r.Building],
            IsActive = r.IsActive,
            RoomFeatures = r.Features.Select(code => new RoomFeature { FeatureId = featureIds[code] }).ToList(),
        }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>One blackout on A101, next Monday 08:00–12:00 campus time, created by the Facilities Officer.</summary>
    private static async Task SeedBlackoutsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.RoomBlackouts.AnyAsync(ct))
            return;

        var officerId = await db.Users.Where(u => u.Email == "perera@campusspace.local").Select(u => (long?)u.Id).SingleOrDefaultAsync(ct);
        var roomId = await db.Rooms.Where(r => r.Code == DemoBlackoutRoom).Select(r => (long?)r.Id).SingleOrDefaultAsync(ct);
        if (officerId is null || roomId is null)
            return;

        var today = DateTimeOffset.UtcNow.ToOffset(CampusOffset).Date;
        var daysToMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var monday = today.AddDays(daysToMonday == 0 ? 7 : daysToMonday);
        var start = new DateTimeOffset(monday.AddHours(8), CampusOffset);
        var end = new DateTimeOffset(monday.AddHours(12), CampusOffset);

        db.RoomBlackouts.Add(new RoomBlackout
        {
            RoomId = roomId.Value,
            TimeRange = new NpgsqlRange<DateTime>(start.UtcDateTime, true, end.UtcDateTime, false),
            Reason = DemoBlackoutReason,
            CreatedById = officerId.Value,
        });
        await db.SaveChangesAsync(ct);
    }
}
