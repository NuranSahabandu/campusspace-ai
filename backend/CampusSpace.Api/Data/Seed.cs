using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Data;

/// <summary>
/// Development seed data. Each step checks its own table (users by email, clubs by "is the table empty"),
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

    public static async Task SeedAsync(AppDbContext db, string demoPassword, CancellationToken ct = default)
    {
        await SeedUsersAsync(db, demoPassword, ct);
        await SeedClubsAsync(db, ct);
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
}
