using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class AuditPersistenceTests(PostgresFixture fixture)
{
    private static User NewUser(string role = Roles.Student) => new()
    {
        FullName = "Audited User",
        Email = $"{Guid.NewGuid():N}@campus.test",
        PasswordHash = "not-a-real-hash",
        Role = role,
    };

    /// <summary>A context whose ICurrentUser is <paramref name="actorId"/>, as if inside that user's request.</summary>
    private AppDbContext CreateDb(long? actorId = null)
    {
        var options = fixture.Factory.Services.GetRequiredService<DbContextOptions<AppDbContext>>();
        return new AppDbContext(options, new FakeCurrentUser(actorId));
    }

    private async Task<long> CreateActorAsync()
    {
        await using var db = CreateDb();
        var admin = NewUser(Roles.Admin);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin.Id;
    }

    private async Task<List<AuditLog>> LogsForAsync(string entityType, string entityId)
    {
        await using var db = CreateDb();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    private static string[] Changed(AuditLog log) =>
        JsonDocument.Parse(log.DetailsJson).RootElement.GetProperty("changed").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task Insert_update_and_delete_write_Created_Updated_Deleted_with_the_actor()
    {
        var actorId = await CreateActorAsync();
        await using var db = CreateDb(actorId);
        var user = NewUser();

        db.Users.Add(user);
        await db.SaveChangesAsync();
        user.FullName = "Renamed";
        user.Role = Roles.Lecturer;
        await db.SaveChangesAsync();
        db.Users.Remove(user);
        await db.SaveChangesAsync();

        var logs = await LogsForAsync(nameof(User), user.Id.ToString());
        logs.Select(l => l.Action).Should().Equal(AuditActions.Created, AuditActions.Updated, AuditActions.Deleted);
        logs.Should().OnlyContain(l => l.UserId == actorId && l.At.Kind == DateTimeKind.Utc);
        Changed(logs[0]).Should().BeEquivalentTo("FullName", "Email", "Role", "IsActive");
        Changed(logs[1]).Should().BeEquivalentTo("FullName", "Role");
        logs[2].DetailsJson.Should().Be("{}");
    }

    [Fact]
    public async Task Details_never_name_or_contain_the_password_hash()
    {
        await using var db = CreateDb();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.PasswordHash = "another-not-real-hash";
        await db.SaveChangesAsync();

        var logs = await LogsForAsync(nameof(User), user.Id.ToString());
        logs.Select(l => l.Action).Should().Equal(AuditActions.Created, AuditActions.Updated);
        Changed(logs[1]).Should().BeEmpty();
        // Across the whole table, not just this user: no row may ever carry the name or a hash value.
        var leaks = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM "AuditLogs"
            WHERE "DetailsJson"::text ILIKE '%passwordhash%'
               OR "DetailsJson"::text LIKE {"%" + user.PasswordHash + "%"}
            """).SingleAsync();
        leaks.Should().Be(0);
    }

    [Fact]
    public async Task Outside_a_request_UserId_is_null()
    {
        await using var db = CreateDb(actorId: null);
        var user = NewUser();

        db.Users.Add(user);
        await db.SaveChangesAsync();

        (await LogsForAsync(nameof(User), user.Id.ToString())).Single().UserId.Should().BeNull();
    }

    [Fact]
    public async Task A_rolled_back_outer_transaction_discards_both_the_change_and_its_audit_row()
    {
        await using var db = CreateDb();
        var user = NewUser();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var check = CreateDb();
        (await check.Users.AnyAsync(u => u.Email == user.Email)).Should().BeFalse();
        (await LogsForAsync(nameof(User), user.Id.ToString())).Should().BeEmpty();
    }

    [Fact]
    public async Task Composite_keys_are_joined_with_a_colon_and_audit_rows_are_not_audited()
    {
        await using var db = CreateDb();
        var user = NewUser();
        var club = new Club { Name = $"Audit Club {Guid.NewGuid():N}" };
        db.AddRange(user, club);
        await db.SaveChangesAsync();

        db.ClubMembers.Add(new ClubMember { ClubId = club.Id, UserId = user.Id, JoinedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        (await LogsForAsync(nameof(ClubMember), $"{club.Id}:{user.Id}")).Should().ContainSingle();
        (await db.AuditLogs.CountAsync(a => a.EntityType == nameof(AuditLog))).Should().Be(0);
    }
}
