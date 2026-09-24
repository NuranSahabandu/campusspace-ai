using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class UserPersistenceTests(PostgresFixture fixture)
{
    // The container is shared across tests, so every user gets a unique email.
    private static User NewUser(string role = Roles.Student) => new()
    {
        FullName = "Test User",
        Email = $"{Guid.NewGuid():N}@campus.test",
        PasswordHash = "not-a-real-hash",
        Role = role,
    };

    private AppDbContext CreateDb(out IServiceScope scope)
    {
        scope = fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    [Fact]
    public async Task Insert_sets_utc_timestamps_and_update_changes_only_UpdatedAt()
    {
        var db = CreateDb(out var scope);
        using var _ = scope;
        var user = NewUser();

        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        user.UpdatedAt.Should().Be(user.CreatedAt);
        var createdAt = user.CreatedAt;

        user.FullName = "Renamed";
        await db.SaveChangesAsync();

        user.UpdatedAt.Should().BeAfter(createdAt);
        var reloaded = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        reloaded.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task IsActive_defaults_to_true_and_an_explicit_false_is_stored()
    {
        var db = CreateDb(out var scope);
        using var _ = scope;
        var active = NewUser();
        var inactive = NewUser();
        inactive.IsActive = false;

        db.Users.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var stored = await db.Users.AsNoTracking()
            .Where(u => u.Id == active.Id || u.Id == inactive.Id)
            .ToDictionaryAsync(u => u.Id, u => u.IsActive);
        stored[active.Id].Should().BeTrue();
        stored[inactive.Id].Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_with_unique_violation()
    {
        var db = CreateDb(out var scope);
        using var _ = scope;
        var first = NewUser();
        db.Users.Add(first);
        await db.SaveChangesAsync();

        var duplicate = NewUser();
        duplicate.Email = first.Email;
        db.Users.Add(duplicate);
        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Unknown_role_is_rejected_by_check_constraint()
    {
        var db = CreateDb(out var scope);
        using var _ = scope;
        db.Users.Add(NewUser(role: "Janitor"));

        var act = () => db.SaveChangesAsync();

        var inner = (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which;
        inner.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        inner.ConstraintName.Should().Be("CK_Users_Role");
    }
}
