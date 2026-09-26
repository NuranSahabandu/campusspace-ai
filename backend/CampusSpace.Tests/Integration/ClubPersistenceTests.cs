using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class ClubPersistenceTests(PostgresFixture fixture)
{
    private static User NewStudent() => new()
    {
        FullName = "Club Student",
        Email = $"{Guid.NewGuid():N}@campus.test",
        PasswordHash = "not-a-real-hash",
        Role = Roles.Student,
    };

    [Fact]
    public async Task Database_rejects_a_second_representative_even_without_the_service()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (first, second) = (NewStudent(), NewStudent());
        var club = new Club { Name = $"Club {Guid.NewGuid():N}" };
        db.AddRange(first, second, club);
        await db.SaveChangesAsync();
        db.ClubMembers.Add(new ClubMember { ClubId = club.Id, UserId = first.Id, IsRepresentative = true, JoinedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // Straight through the DbContext: ClubService's "unset the old rep first" never runs.
        db.ClubMembers.Add(new ClubMember { ClubId = club.Id, UserId = second.Id, IsRepresentative = true, JoinedAt = DateTime.UtcNow });
        var act = () => db.SaveChangesAsync();

        var inner = (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which;
        inner.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        inner.ConstraintName.Should().Be(ClubMemberConfiguration.OneRepresentativeIndex);
    }

    [Fact]
    public async Task Several_non_representative_members_are_allowed()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (first, second) = (NewStudent(), NewStudent());
        var club = new Club { Name = $"Club {Guid.NewGuid():N}" };
        db.AddRange(first, second, club);
        await db.SaveChangesAsync();

        db.ClubMembers.AddRange(
            new ClubMember { ClubId = club.Id, UserId = first.Id, JoinedAt = DateTime.UtcNow },
            new ClubMember { ClubId = club.Id, UserId = second.Id, JoinedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        (await db.ClubMembers.CountAsync(m => m.ClubId == club.Id)).Should().Be(2);
    }
}
