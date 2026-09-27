using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class PolicySettingsServiceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GetAsync_works_on_a_migrated_database_without_the_seed()
    {
        await using var db = PostgresFixture.CreateDbContext(await fixture.CreateDatabaseAsync());

        var policy = await new PolicySettingsService(db, new FakeCurrentUser(null)).GetAsync();

        policy.MinLeadTimeHours.Should().Be(48);
        policy.MaxCapacityRatio.Should().Be(3m);
        policy.SlotGranularityMinutes.Should().Be(30);
        policy.OpeningHours[DayOfWeek.Sunday].Should().BeNull();
        policy.OpeningHours[DayOfWeek.Friday]!.Close.Should().Be(new TimeOnly(20, 0));
    }

    [Fact]
    public async Task GetAsync_reads_the_database_on_every_call()
    {
        await using var db = PostgresFixture.CreateDbContext(await fixture.CreateDatabaseAsync());
        var service = new PolicySettingsService(db, new FakeCurrentUser(null));
        await service.GetAsync();

        await db.PolicySettings.Where(s => s.Key == PolicyKeys.MaxOpenRequests).ExecuteUpdateAsync(s => s.SetProperty(p => p.Value, "7"));

        (await service.GetAsync()).MaxOpenRequests.Should().Be(7);
    }

    [Fact]
    public async Task GetAsync_throws_when_a_key_is_missing()
    {
        await using var db = PostgresFixture.CreateDbContext(await fixture.CreateDatabaseAsync());
        await db.PolicySettings.Where(s => s.Key == PolicyKeys.MaxOpenRequests).ExecuteDeleteAsync();

        var act = () => new PolicySettingsService(db, new FakeCurrentUser(null)).GetAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{PolicyKeys.MaxOpenRequests}*");
    }

    [Fact]
    public async Task GetAsync_throws_when_a_value_does_not_parse()
    {
        await using var db = PostgresFixture.CreateDbContext(await fixture.CreateDatabaseAsync());
        await db.PolicySettings.Where(s => s.Key == PolicyKeys.MinLeadTimeHours).ExecuteUpdateAsync(s => s.SetProperty(p => p.Value, "soon"));

        var act = () => new PolicySettingsService(db, new FakeCurrentUser(null)).GetAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{PolicyKeys.MinLeadTimeHours}*");
    }
}
