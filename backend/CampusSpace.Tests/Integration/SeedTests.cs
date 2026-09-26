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
    private async Task<AppDbContext> CreateEmptyDatabaseAsync()
    {
        var name = $"seed_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ToString();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();
        return db;
    }

    [Fact]
    public async Task Seeds_one_active_account_per_role_with_the_demo_password_and_is_idempotent()
    {
        await using var db = await CreateEmptyDatabaseAsync();

        await Seed.SeedAsync(db, DemoPassword);
        await Seed.SeedAsync(db, DemoPassword);

        var users = await db.Users.AsNoTracking().ToListAsync();
        users.Should().HaveCount(5);
        users.Select(u => u.Role).Should().BeEquivalentTo(Roles.All);
        users.Should().OnlyContain(u => u.IsActive && u.Email == u.Email.ToLowerInvariant());
        users.Select(u => BCrypt.Net.BCrypt.Verify(DemoPassword, u.PasswordHash)).Should().AllBeEquivalentTo(true);
        users.Single(u => u.Role == Roles.Admin).Email.Should().Be("admin@campusspace.local");
        users.Single(u => u.Role == Roles.FacilitiesOfficer).FullName.Should().Be("Mr. Perera");
    }
}
