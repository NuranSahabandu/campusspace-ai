using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Integration;

/// <summary>GetEffectiveRuleAsync, which the Phase 2 quotation uses. Each test has its own database and a fixed clock.</summary>
[Collection(PostgresCollection.Name)]
public class PricingRuleServiceTests(PostgresFixture fixture)
{
    private static readonly TimeSpan Campus = TimeSpan.FromMinutes(330);

    private async Task<(AppDbContext Db, PricingRuleService Service)> CreateAsync()
    {
        var db = PostgresFixture.CreateDbContext(await fixture.CreateDatabaseAsync());
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, Campus));
        return (db, new PricingRuleService(db, clock));
    }

    [Fact]
    public async Task Picks_the_latest_rule_starting_on_or_before_the_booking_date()
    {
        var (db, service) = await CreateAsync();
        await using var _ = db;
        var jan = await PricingTestData.InsertAsync(db, RoomTypes.ComputerLab, RequesterRoles.Student, new DateOnly(2026, 1, 1), 1500m);
        var mar = await PricingTestData.InsertAsync(db, RoomTypes.ComputerLab, RequesterRoles.Student, new DateOnly(2026, 3, 1), 1800m);
        await PricingTestData.InsertAsync(db, RoomTypes.ComputerLab, RequesterRoles.Lecturer, new DateOnly(2026, 2, 1), 0m, exempt: true);
        await PricingTestData.InsertAsync(db, RoomTypes.LectureHall, RequesterRoles.Student, new DateOnly(2026, 2, 1), 1000m);

        Task<Api.Dtos.Pricing.EffectivePricingRule?> At(int month, int day, int hour = 10) => service.GetEffectiveRuleAsync(
            RoomTypes.ComputerLab, RequesterRoles.Student, new DateTimeOffset(2026, month, day, hour, 0, 0, Campus));

        (await At(2, 28))!.Id.Should().Be(jan);
        (await At(3, 1))!.Should().Match<Api.Dtos.Pricing.EffectivePricingRule>(r => r.Id == mar && r.HourlyRate == 1800m && !r.IsExempt);
        (await At(12, 31))!.Id.Should().Be(mar);
        (await At(1, 1, 8))!.Id.Should().Be(jan);
    }

    [Fact]
    public async Task Returns_null_before_the_first_rule_or_for_a_pair_with_no_rules()
    {
        var (db, service) = await CreateAsync();
        await using var _ = db;
        await PricingTestData.InsertAsync(db, RoomTypes.ComputerLab, RequesterRoles.Student, new DateOnly(2026, 1, 1), 1500m);

        (await service.GetEffectiveRuleAsync(RoomTypes.ComputerLab, RequesterRoles.Student,
            new DateTimeOffset(2025, 12, 31, 12, 0, 0, Campus))).Should().BeNull();
        (await service.GetEffectiveRuleAsync(RoomTypes.Auditorium, RequesterRoles.Student,
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, Campus))).Should().BeNull();
    }

    [Fact]
    public async Task A_booking_at_half_past_midnight_campus_time_uses_its_campus_day_not_the_utc_day()
    {
        var (db, service) = await CreateAsync();
        await using var _ = db;
        var feb = await PricingTestData.InsertAsync(db, RoomTypes.SeminarRoom, RequesterRoles.Student, new DateOnly(2026, 2, 1), 500m);
        var mar = await PricingTestData.InsertAsync(db, RoomTypes.SeminarRoom, RequesterRoles.Student, new DateOnly(2026, 3, 1), 600m);
        // 2026-03-01 00:30 campus time is 2026-02-28 19:00 UTC.
        var start = new DateTimeOffset(2026, 2, 28, 19, 0, 0, TimeSpan.Zero);

        var rule = await service.GetEffectiveRuleAsync(RoomTypes.SeminarRoom, RequesterRoles.Student, start);

        rule!.Id.Should().Be(mar);
        (await service.GetEffectiveRuleAsync(RoomTypes.SeminarRoom, RequesterRoles.Student, start.AddMinutes(-31)))!.Id.Should().Be(feb);
    }

    [Fact]
    public async Task Today_comes_from_the_clock_in_campus_time()
    {
        var (db, service) = await CreateAsync();
        await using var _ = db;
        // The clock is 2026-01-15 on campus.
        var today = await PricingTestData.InsertAsync(db, RoomTypes.Auditorium, RequesterRoles.Student, new DateOnly(2026, 1, 15), 3000m);
        var tomorrow = await PricingTestData.InsertAsync(db, RoomTypes.Auditorium, RequesterRoles.Lecturer, new DateOnly(2026, 1, 16), 0m, exempt: true);

        (await service.GetAsync(today))!.Status.Should().Be(PricingRuleStatuses.Current);
        (await service.GetAsync(tomorrow))!.Status.Should().Be(PricingRuleStatuses.Scheduled);
        (await service.DeleteAsync(tomorrow)).Should().BeTrue();
    }
}
