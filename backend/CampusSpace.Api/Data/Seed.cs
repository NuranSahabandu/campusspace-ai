using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace CampusSpace.Api.Data;

/// <summary>
/// Seed data in two modes. <see cref="SeedReferenceAsync"/> (Production): the reference and demo data examiners need
/// (demo accounts, clubs, features, buildings, rooms, equipment types, items and substitutes, pricing rules, policy
/// settings) and nothing else. <see cref="SeedAsync"/> (Development): the same plus the demo blackout and the demo
/// booking requests. Both only SELECT and INSERT (DML rights are enough). Each step checks its own table (users by email, clubs by "is the table empty",
/// features, buildings, rooms and equipment types by code, equipment items by asset tag, substitutes by pair,
/// blackouts by "is the table empty",
/// pricing rules by (room type, role, start date), policy settings by key and never overwriting an edited value,
/// booking requests by (requester, purpose)),
/// so startup can call this every time and it also fills in a database that already has some rows.
/// </summary>
public static class Seed
{
    /// <summary>
    /// Demo accounts. All share the demo password (config key Seed:DemoPassword): the public one in
    /// appsettings.Development.json, and in Production an environment variable that is never in Git.
    /// </summary>
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

    /// <summary>
    /// Equipment types, each with a short asset-tag prefix and an item count (60 items in all). CoveredBy names the room
    /// feature that makes the type unnecessary (addendum Change B). The demo quote relies on MIC-WIRELESS costing 500.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name, string Category, decimal Fee, string? CoveredBy, string TagPrefix, int Count)> DemoEquipmentTypes =
    [
        ("MIC-WIRELESS", "Wireless microphone", EquipmentCategories.Audio, 500m, null, "MICW", 8),
        ("MIC-WIRED", "Wired microphone", EquipmentCategories.Audio, 200m, null, "MICD", 8),
        ("SPEAKER-PORTABLE", "Portable speaker", EquipmentCategories.Audio, 1000m, "sound_system", "SPK", 4),
        ("PROJ-PORTABLE", "Portable projector", EquipmentCategories.Visual, 1500m, "projector", "PROJ", 5),
        ("SCREEN-PORTABLE", "Portable projection screen", EquipmentCategories.Visual, 500m, null, "SCR", 4),
        ("WHITEBOARD-MOBILE", "Mobile whiteboard", EquipmentCategories.Presentation, 300m, "whiteboard", "WB", 4),
        ("CLICKER", "Presentation clicker", EquipmentCategories.Presentation, 100m, null, "CLK", 8),
        ("LAPTOP", "Laptop", EquipmentCategories.Computing, 750m, null, "LAP", 8),
        ("CAMERA-VIDEO", "Video camera", EquipmentCategories.Visual, 1500m, null, "CAM", 3),
        ("EXT-CABLE", "Extension cable", EquipmentCategories.Accessory, 0m, null, "EXT", 8),
    ];

    /// <summary>The items that are not Good/Available.</summary>
    public static readonly IReadOnlyDictionary<string, (string Condition, string Status)> DemoEquipmentItemStates =
        new Dictionary<string, (string, string)>
        {
            ["EQ-MICW-008"] = (EquipmentConditions.Damaged, EquipmentItemStatuses.UnderRepair),
            ["EQ-PROJ-005"] = (EquipmentConditions.MinorWear, EquipmentItemStatuses.Available),
            ["EQ-LAP-008"] = (EquipmentConditions.Good, EquipmentItemStatuses.Retired),
        };

    /// <summary>Directional pairs: Type can be replaced by Substitute.</summary>
    public static readonly IReadOnlyList<(string Type, string Substitute)> DemoEquipmentSubstitutes =
    [
        ("MIC-WIRELESS", "MIC-WIRED"),
        ("MIC-WIRED", "MIC-WIRELESS"),
    ];

    /// <summary>EQ-MICW-001 style: a per-type prefix and a zero-padded number.</summary>
    public static string DemoAssetTag(string prefix, int number) => $"EQ-{prefix}-{number:000}";

    public const string DemoBlackoutRoom = "A101";
    public const string DemoBlackoutReason = "Projector maintenance";

    /// <summary>
    /// Room rates from 2026-01-01. Lecturers are exempt. The demo quote relies on ComputerLab/Student costing 1500
    /// (3 h x 1500 + 2 microphones x 500 = 5,500).
    /// </summary>
    public static readonly DateOnly DemoPricingFrom = new(2026, 1, 1);

    public static readonly IReadOnlyList<(string RoomType, string Role, decimal HourlyRate, bool IsExempt)> DemoPricingRules =
    [
        (RoomTypes.ComputerLab, RequesterRoles.Student, 1500m, false),
        (RoomTypes.LectureHall, RequesterRoles.Student, 1000m, false),
        (RoomTypes.SeminarRoom, RequesterRoles.Student, 500m, false),
        (RoomTypes.Auditorium, RequesterRoles.Student, 3000m, false),
        (RoomTypes.ComputerLab, RequesterRoles.Lecturer, 0m, true),
        (RoomTypes.LectureHall, RequesterRoles.Lecturer, 0m, true),
        (RoomTypes.SeminarRoom, RequesterRoles.Lecturer, 0m, true),
        (RoomTypes.Auditorium, RequesterRoles.Lecturer, 0m, true),
    ];

    /// <summary>
    /// Two Submitted requests so the request lists have data. Neither is Kavindi's: she submits the demo request herself
    /// and needs all her open-request slots. Nethmi represents the Drama Society.
    /// </summary>
    public static readonly IReadOnlyList<(string Email, string? Club, string Purpose, int Attendees, string[] Features, (string Type, int Quantity)[] Equipment, decimal Budget)> DemoBookingRequests =
    [
        ("nethmi@campusspace.local", "Drama Society", "Drama Society rehearsal", 30, ["smart_board", "ac"], [("MIC-WIRED", 1)], 3000m),
        ("lecturer@campusspace.local", null, "Guest lecture: AI in agriculture", 120, ["projector", "sound_system"], [("MIC-WIRELESS", 2)], 0m),
    ];

    /// <summary>Development: the reference data, plus the demo blackout and the demo booking requests.</summary>
    public static async Task SeedAsync(AppDbContext db, string demoPassword, CancellationToken ct = default)
    {
        await SeedReferenceAsync(db, demoPassword, ct);
        await SeedBlackoutsAsync(db, ct);
        await SeedBookingRequestsAsync(db, ct);
    }

    /// <summary>
    /// Production: the reference and demo data only (no requests, bookings, blackouts, loans or agent runs). The demo
    /// accounts' password comes from the environment (Seed__DemoPassword), never from Git.
    /// </summary>
    public static async Task SeedReferenceAsync(AppDbContext db, string demoPassword, CancellationToken ct = default)
    {
        await SeedUsersAsync(db, demoPassword, ct);
        await SeedClubsAsync(db, ct);
        await SeedFeaturesAsync(db, ct);
        await SeedBuildingsAsync(db, ct);
        await SeedRoomsAsync(db, ct);
        await SeedEquipmentTypesAsync(db, ct);
        await SeedEquipmentItemsAsync(db, ct);
        await SeedEquipmentSubstitutesAsync(db, ct);
        await SeedPricingRulesAsync(db, ct);
        await SeedPolicySettingsAsync(db, ct);
    }

    /// <summary>
    /// Adds each demo request that is missing (same requester and purpose), on a weekday about four weeks after the seed
    /// runs, 10:00–12:00 campus time, so it still satisfies the Phase 2 timing rules. The dates are fixed when the row is
    /// inserted; resetting the dev database gives new ones. A request whose requester, club or type is missing is skipped.
    /// </summary>
    public static async Task SeedBookingRequestsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var day = CampusTime.Today(TimeProvider.System).AddDays(28);
        if (day.DayOfWeek == DayOfWeek.Saturday)
            day = day.AddDays(2);
        else if (day.DayOfWeek == DayOfWeek.Sunday)
            day = day.AddDays(1);
        var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), CampusTime.Offset);
        var end = start.AddHours(2);

        var emails = DemoBookingRequests.Select(r => r.Email).ToList();
        var userIds = await db.Users.Where(u => emails.Contains(u.Email)).ToDictionaryAsync(u => u.Email, u => u.Id, ct);
        var clubIds = await db.Clubs.ToDictionaryAsync(c => c.Name, c => c.Id, ct);
        var typeIds = await db.EquipmentTypes.ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var existing = (await db.BookingRequests.Select(r => new { r.RequesterId, r.Purpose }).ToListAsync(ct))
            .Select(r => (r.RequesterId, r.Purpose)).ToHashSet();
        var stateMachine = new RequestStateMachine(TimeProvider.System);

        foreach (var r in DemoBookingRequests)
        {
            if (!userIds.TryGetValue(r.Email, out var requesterId) || existing.Contains((requesterId, r.Purpose)))
                continue;
            long? clubId = null;
            if (r.Club is not null)
            {
                if (!clubIds.TryGetValue(r.Club, out var id))
                    continue;
                clubId = id;
            }
            if (r.Equipment.Any(e => !typeIds.ContainsKey(e.Type)))
                continue;

            var request = new BookingRequest
            {
                RequesterId = requesterId,
                ClubId = clubId,
                Purpose = r.Purpose,
                Attendees = r.Attendees,
                RequestedStart = start.UtcDateTime,
                RequestedEnd = end.UtcDateTime,
                BudgetLkr = r.Budget,
                RequiredFeatures = [.. r.Features],
                EquipmentLines = r.Equipment.Select(e => new RequestedEquipmentLine { TypeId = typeIds[e.Type], Quantity = e.Quantity }).ToList(),
            };
            stateMachine.Start(request, requesterId);
            db.BookingRequests.Add(request);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds the missing demo rules. A rule that exists (same room type, role and start date) is left as it is.</summary>
    public static async Task SeedPricingRulesAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existing = (await db.PricingRules.Where(r => r.ValidFrom == DemoPricingFrom)
                .Select(r => new { r.RoomType, r.RequesterRole }).ToListAsync(ct))
            .Select(r => (r.RoomType, r.RequesterRole)).ToHashSet();
        db.PricingRules.AddRange(DemoPricingRules
            .Where(r => !existing.Contains((r.RoomType, r.Role)))
            .Select(r => new PricingRule
            {
                RoomType = r.RoomType, RequesterRole = r.Role, HourlyRate = r.HourlyRate, IsExempt = r.IsExempt, ValidFrom = DemoPricingFrom,
            }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds any missing policy key with its default. The migration already inserts all of them, so this only
    /// matters if a key was removed. Never overwrites an existing value: an officer's edit wins.
    /// </summary>
    public static async Task SeedPolicySettingsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existing = await db.PolicySettings.Select(s => s.Key).ToListAsync(ct);
        db.PolicySettings.AddRange(PolicySettingDefaults.All
            .Where(d => !existing.Contains(d.Key))
            .Select(d => new PolicySetting { Key = d.Key, ValueType = d.ValueType, Value = d.Value, Description = d.Description }));
        await db.SaveChangesAsync(ct);
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

    /// <summary>Adds missing types. Covering features are referenced by code, which features were seeded with above.</summary>
    private static async Task SeedEquipmentTypesAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.EquipmentTypes.Select(t => t.Code).ToListAsync(ct);
        db.EquipmentTypes.AddRange(DemoEquipmentTypes.Where(t => !existing.Contains(t.Code)).Select(t => new EquipmentType
        {
            Code = t.Code,
            Name = t.Name,
            Category = t.Category,
            FeePerBooking = t.Fee,
            CoveredByFeatureCode = t.CoveredBy,
        }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds missing items by asset tag. Existing items keep their status and condition.</summary>
    private static async Task SeedEquipmentItemsAsync(AppDbContext db, CancellationToken ct)
    {
        var typeIds = await db.EquipmentTypes.ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var existing = (await db.EquipmentItems.Select(i => i.AssetTag).ToListAsync(ct)).ToHashSet();
        var missing =
            from t in DemoEquipmentTypes
            from n in Enumerable.Range(1, t.Count)
            let tag = DemoAssetTag(t.TagPrefix, n)
            where !existing.Contains(tag)
            select new { t.Code, Tag = tag };

        db.EquipmentItems.AddRange(missing.Select(m =>
        {
            var (condition, status) = DemoEquipmentItemStates.GetValueOrDefault(
                m.Tag, (EquipmentConditions.Good, EquipmentItemStatuses.Available));
            return new EquipmentItem { TypeId = typeIds[m.Code], AssetTag = m.Tag, Condition = condition, Status = status };
        }));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedEquipmentSubstitutesAsync(AppDbContext db, CancellationToken ct)
    {
        var typeIds = await db.EquipmentTypes.ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var existing = (await db.EquipmentSubstitutes.Select(s => new { s.TypeId, s.SubstituteTypeId }).ToListAsync(ct))
            .Select(s => (s.TypeId, s.SubstituteTypeId)).ToHashSet();

        db.EquipmentSubstitutes.AddRange(DemoEquipmentSubstitutes
            .Select(p => (TypeId: typeIds[p.Type], SubstituteTypeId: typeIds[p.Substitute]))
            .Where(p => !existing.Contains(p))
            .Select(p => new EquipmentSubstitute { TypeId = p.TypeId, SubstituteTypeId = p.SubstituteTypeId }));
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

        var today = DateTimeOffset.UtcNow.ToOffset(CampusTime.Offset).Date;
        var daysToMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var monday = today.AddDays(daysToMonday == 0 ? 7 : daysToMonday);
        var start = new DateTimeOffset(monday.AddHours(8), CampusTime.Offset);
        var end = new DateTimeOffset(monday.AddHours(12), CampusTime.Offset);

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
