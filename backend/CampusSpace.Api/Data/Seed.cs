using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Data;

/// <summary>
/// Development seed data. Each block only runs when its table is empty, so startup can call this every time.
/// </summary>
public static class Seed
{
    /// <summary>One demo account per role. All share the public demo password (config key Seed:DemoPassword).</summary>
    public static readonly IReadOnlyList<(string Email, string FullName, string Role)> DemoUsers =
    [
        ("kavindi@campusspace.local", "Kavindi Perera", Roles.Student),
        ("lecturer@campusspace.local", "Dr. Nimal Fernando", Roles.Lecturer),
        ("tech@campusspace.local", "Sunil Jayasinghe", Roles.LabTechnician),
        ("perera@campusspace.local", "Mr. Perera", Roles.FacilitiesOfficer),
        ("admin@campusspace.local", "System Admin", Roles.Admin),
    ];

    public static async Task SeedAsync(AppDbContext db, string demoPassword, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(ct))
        {
            // Hash once: every demo account has the same password.
            var hash = BCrypt.Net.BCrypt.HashPassword(demoPassword);
            db.Users.AddRange(DemoUsers.Select(u => new User
            {
                Email = u.Email,
                FullName = u.FullName,
                Role = u.Role,
                PasswordHash = hash,
            }));
            await db.SaveChangesAsync(ct);
        }
    }
}
