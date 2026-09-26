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
}
