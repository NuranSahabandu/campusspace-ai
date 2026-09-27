using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class SeedTests(PostgresFixture fixture)
{
    private const string DemoPassword = "demo-password-1";

    /// <summary>A fresh, migrated database on the shared container, because the default one already has users.</summary>
    private async Task<AppDbContext> CreateEmptyDatabaseAsync() =>
        PostgresFixture.CreateDbContext(await fixture.CreateDatabaseAsync());

    [Fact]
    public async Task Seeds_every_demo_account_active_with_the_demo_password_and_is_idempotent()
    {
        await using var db = await CreateEmptyDatabaseAsync();

        await Seed.SeedAsync(db, DemoPassword);
        await Seed.SeedAsync(db, DemoPassword);

        var users = await db.Users.AsNoTracking().ToListAsync();
        users.Should().HaveCount(Seed.DemoUsers.Count);
        users.Select(u => u.Role).Distinct().Should().BeEquivalentTo(Roles.All);
        users.Count(u => u.Role == Roles.Student).Should().Be(4);
        users.Should().OnlyContain(u => u.IsActive && u.Email == u.Email.ToLowerInvariant());
        users.Select(u => BCrypt.Net.BCrypt.Verify(DemoPassword, u.PasswordHash)).Should().AllBeEquivalentTo(true);
        users.Single(u => u.Role == Roles.Admin).Email.Should().Be("admin@campusspace.local");
        users.Single(u => u.Role == Roles.FacilitiesOfficer).FullName.Should().Be("Mr. Perera");
    }

    [Fact]
    public async Task On_a_database_that_already_has_users_it_adds_the_new_students_and_clubs_once()
    {
        await using var db = await CreateEmptyDatabaseAsync();
        // The Phase 0 state: the five original demo accounts already exist.
        db.Users.AddRange(Seed.DemoUsers.Take(5).Select(u => new User
        {
            Email = u.Email, FullName = u.FullName, Role = u.Role, PasswordHash = "existing-hash",
        }));
        await db.SaveChangesAsync();

        await Seed.SeedAsync(db, DemoPassword);
        await Seed.SeedAsync(db, DemoPassword);

        var users = await db.Users.AsNoTracking().ToListAsync();
        users.Select(u => u.Email).Should().OnlyHaveUniqueItems().And.HaveCount(Seed.DemoUsers.Count);
        users.Single(u => u.Email == "admin@campusspace.local").PasswordHash.Should().Be("existing-hash");
        users.Select(u => u.FullName).Should().Contain(["Ishan Silva", "Nethmi Rajapaksa", "Tharindu Wickramasinghe"]);

        var clubs = await db.Clubs.AsNoTracking().Include(c => c.Members).ThenInclude(m => m.User).ToListAsync();
        clubs.Select(c => c.Name).Should().BeEquivalentTo("Robotics Club", "Drama Society", "IEEE Student Branch");
        clubs.Should().OnlyContain(c => c.IsActive
            && c.Members.Count(m => m.IsRepresentative) == 1
            && c.Members.Count(m => !m.IsRepresentative) >= 1
            && c.Members.All(m => m.User.Role == Roles.Student || m.User.Role == Roles.Lecturer));
        clubs.Single(c => c.Name == "Robotics Club").Members.Single(m => m.IsRepresentative).User.FullName
            .Should().Be("Kavindi Perera");
        (await db.ClubMembers.CountAsync()).Should().Be(Seed.DemoClubs.Sum(c => c.MemberEmails.Length));
    }

    [Fact]
    public async Task Seeds_facilities_once_with_the_walkthrough_labs_and_one_blackout()
    {
        await using var db = await CreateEmptyDatabaseAsync();

        await Seed.SeedAsync(db, DemoPassword);
        await Seed.SeedAsync(db, DemoPassword);

        (await db.Features.Select(f => f.Code).ToListAsync()).Should().BeEquivalentTo(
            ["projector", "computers", "whiteboard", "ac", "sound_system", "smart_board"]);
        (await db.Buildings.Select(b => b.Code).ToListAsync()).Should().BeEquivalentTo(["MB", "NB", "EB"]);

        var rooms = await db.Rooms.AsNoTracking()
            .Include(r => r.Building).Include(r => r.RoomFeatures).ThenInclude(rf => rf.Feature).ToListAsync();
        rooms.Should().HaveCount(Seed.DemoRooms.Count).And.HaveCountGreaterThanOrEqualTo(15);
        rooms.Select(r => r.Code).Should().OnlyHaveUniqueItems();
        rooms.Select(r => r.Type).Distinct().Should().BeEquivalentTo(RoomTypes.All);
        string[] FeaturesOf(string code) => rooms.Single(r => r.Code == code).RoomFeatures.Select(rf => rf.Feature.Code).ToArray();
        FeaturesOf("A301").Should().BeEquivalentTo(["computers", "projector", "ac", "whiteboard"]);
        FeaturesOf("A305").Should().BeEquivalentTo(["computers", "whiteboard", "ac"]);
        FeaturesOf("N201").Should().BeEquivalentTo(["computers", "projector", "ac", "smart_board"]);
        rooms.Single(r => r.Code == "A301").Should().Match<Room>(r => r.Building.Code == "MB" && r.Capacity == 48 && r.Type == RoomTypes.ComputerLab);
        rooms.Single(r => r.Code == "A305").Should().Match<Room>(r => r.Building.Code == "MB" && r.Capacity == 50);
        rooms.Single(r => r.Code == "N201").Should().Match<Room>(r => r.Building.Code == "NB" && r.Capacity == 60);
        rooms.Should().Contain(r => r.Type == RoomTypes.Auditorium && r.Capacity >= 200
            && r.RoomFeatures.Any(rf => rf.Feature.Code == "sound_system"));
        rooms.Where(r => r.Type == RoomTypes.LectureHall).Should().OnlyContain(r => r.Capacity >= 80 && r.Capacity <= 150);
        rooms.Where(r => r.Type == RoomTypes.SeminarRoom).Should().OnlyContain(r => r.Capacity >= 15 && r.Capacity <= 30);
        rooms.Count(r => !r.IsActive).Should().Be(1);
        (await db.RoomFeatures.CountAsync()).Should().Be(Seed.DemoRooms.Sum(r => r.Features.Length));

        var blackout = await db.RoomBlackouts.AsNoTracking().Include(b => b.Room).Include(b => b.CreatedBy).SingleAsync();
        blackout.Room.Code.Should().Be(Seed.DemoBlackoutRoom);
        blackout.Room.Type.Should().Be(RoomTypes.LectureHall);
        blackout.Reason.Should().Be("Projector maintenance");
        blackout.CreatedBy.Email.Should().Be("perera@campusspace.local");
        blackout.TimeRange.LowerBound.Kind.Should().Be(DateTimeKind.Utc);
        blackout.TimeRange.LowerBoundIsInclusive.Should().BeTrue();
        blackout.TimeRange.UpperBoundIsInclusive.Should().BeFalse();
        blackout.TimeRange.LowerBound.Should().BeAfter(DateTime.UtcNow).And.BeBefore(DateTime.UtcNow.AddDays(8));
        // Monday 08:00-12:00 at UTC+05:30.
        var campusStart = new DateTimeOffset(blackout.TimeRange.LowerBound).ToOffset(TimeSpan.FromMinutes(330));
        campusStart.DayOfWeek.Should().Be(DayOfWeek.Monday);
        campusStart.TimeOfDay.Should().Be(TimeSpan.FromHours(8));
        (blackout.TimeRange.UpperBound - blackout.TimeRange.LowerBound).Should().Be(TimeSpan.FromHours(4));
    }

    [Fact]
    public async Task Seeds_the_equipment_catalogue_once()
    {
        await using var db = await CreateEmptyDatabaseAsync();

        await Seed.SeedAsync(db, DemoPassword);
        await Seed.SeedAsync(db, DemoPassword);

        var types = await db.EquipmentTypes.AsNoTracking().Include(t => t.Items).ToListAsync();
        types.Should().HaveCount(10);
        types.Select(t => t.Category).Distinct().Should().BeEquivalentTo(EquipmentCategories.All);
        types.Single(t => t.Code == "MIC-WIRELESS").FeePerBooking.Should().Be(500m);
        types.Single(t => t.Code == "PROJ-PORTABLE").CoveredByFeatureCode.Should().Be("projector");
        types.Single(t => t.Code == "SPEAKER-PORTABLE").CoveredByFeatureCode.Should().Be("sound_system");
        types.Single(t => t.Code == "WHITEBOARD-MOBILE").CoveredByFeatureCode.Should().Be("whiteboard");
        types.Count(t => t.CoveredByFeatureCode != null).Should().Be(3);
        types.Single(t => t.Code == "EXT-CABLE").FeePerBooking.Should().Be(0m);
        types.Single(t => t.Code == "MIC-WIRELESS").Items.Should().HaveCount(8);

        var items = await db.EquipmentItems.AsNoTracking().ToListAsync();
        items.Should().HaveCount(60);
        items.Select(i => i.AssetTag).Should().Contain(["EQ-MICW-001", "EQ-CAM-003", "EQ-EXT-008"]);
        items.Where(i => i.Condition != EquipmentConditions.Good || i.Status != EquipmentItemStatuses.Available)
            .Select(i => (i.AssetTag, i.Condition, i.Status)).Should().BeEquivalentTo(new[]
            {
                ("EQ-MICW-008", EquipmentConditions.Damaged, EquipmentItemStatuses.UnderRepair),
                ("EQ-PROJ-005", EquipmentConditions.MinorWear, EquipmentItemStatuses.Available),
                ("EQ-LAP-008", EquipmentConditions.Good, EquipmentItemStatuses.Retired),
            });

        var pairs = await db.EquipmentSubstitutes.AsNoTracking()
            .Select(p => new { Type = p.Type.Code, Substitute = p.SubstituteType.Code }).ToListAsync();
        pairs.Select(p => (p.Type, p.Substitute)).Should().BeEquivalentTo(new[] { ("MIC-WIRELESS", "MIC-WIRED"), ("MIC-WIRED", "MIC-WIRELESS") });
    }

    [Fact]
    public async Task Equipment_seed_adds_only_missing_rows_and_never_changes_existing_ones()
    {
        await using var db = await CreateEmptyDatabaseAsync();
        await Seed.SeedAsync(db, DemoPassword);
        var retired = await db.EquipmentItems.SingleAsync(i => i.AssetTag == "EQ-MICW-001");
        retired.Status = EquipmentItemStatuses.Retired;
        db.EquipmentItems.Remove(await db.EquipmentItems.SingleAsync(i => i.AssetTag == "EQ-CLK-004"));
        db.EquipmentSubstitutes.RemoveRange(db.EquipmentSubstitutes);
        await db.SaveChangesAsync();

        await Seed.SeedAsync(db, DemoPassword);

        db.ChangeTracker.Clear();
        (await db.EquipmentItems.CountAsync()).Should().Be(60);
        (await db.EquipmentItems.SingleAsync(i => i.AssetTag == "EQ-MICW-001")).Status.Should().Be(EquipmentItemStatuses.Retired);
        (await db.EquipmentItems.AnyAsync(i => i.AssetTag == "EQ-CLK-004")).Should().BeTrue();
        (await db.EquipmentSubstitutes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Seeds_the_demo_pricing_rules_once()
    {
        await using var db = await CreateEmptyDatabaseAsync();

        await Seed.SeedAsync(db, DemoPassword);
        await Seed.SeedAsync(db, DemoPassword);

        var rules = await db.PricingRules.AsNoTracking().ToListAsync();
        rules.Should().HaveCount(RoomTypes.All.Count * RequesterRoles.All.Count);
        rules.Should().OnlyContain(r => r.ValidFrom == new DateOnly(2026, 1, 1));
        // The demo quote: 3 h x 1500 + 2 microphones x 500 = 5,500.
        rules.Single(r => r.RoomType == RoomTypes.ComputerLab && r.RequesterRole == RequesterRoles.Student).HourlyRate.Should().Be(1500m);
        rules.Where(r => r.RequesterRole == RequesterRoles.Student).Select(r => (r.RoomType, r.HourlyRate)).Should().BeEquivalentTo(new[]
        {
            (RoomTypes.ComputerLab, 1500m), (RoomTypes.LectureHall, 1000m), (RoomTypes.SeminarRoom, 500m), (RoomTypes.Auditorium, 3000m),
        });
        rules.Where(r => r.RequesterRole == RequesterRoles.Lecturer).Should().HaveCount(4).And.OnlyContain(r => r.IsExempt && r.HourlyRate == 0);
    }

    [Fact]
    public async Task Policy_seed_never_overwrites_an_edited_value_and_restores_a_missing_key()
    {
        await using var db = await CreateEmptyDatabaseAsync();
        await Seed.SeedAsync(db, DemoPassword);
        (await db.PolicySettings.SingleAsync(s => s.Key == PolicyKeys.MaxCapacityRatio)).Value = "2";
        db.PolicySettings.Remove(await db.PolicySettings.SingleAsync(s => s.Key == PolicyKeys.MaxOpenRequests));
        await db.SaveChangesAsync();

        await Seed.SeedAsync(db, DemoPassword);

        db.ChangeTracker.Clear();
        (await db.PolicySettings.CountAsync()).Should().Be(PolicyKeys.All.Count);
        (await db.PolicySettings.SingleAsync(s => s.Key == PolicyKeys.MaxCapacityRatio)).Value.Should().Be("2");
        (await db.PolicySettings.SingleAsync(s => s.Key == PolicyKeys.MaxOpenRequests)).Value.Should().Be("3");
    }
}
