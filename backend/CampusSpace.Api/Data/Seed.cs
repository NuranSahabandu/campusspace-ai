using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Data;

/// <summary>
/// Development seed data. Each block only runs when its table is empty, so startup can call this every time.
/// </summary>
public static class Seed
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(ct))
        {
            // TODO(next task): seed one user per role.
        }
    }
}
