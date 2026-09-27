using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// IQuotationCalculator against a real, seeded database of its own (the real A301, MIC-WIRELESS at 500 and the demo
/// pricing rules), with a fixed clock. The demo slot is Tuesday 2026-10-06 14:00–17:00 campus time (§11 step 4).
/// </summary>
[Collection(PostgresCollection.Name)]
public class QuotationCalculatorTests(PostgresFixture fixture)
{
    private static readonly DateOnly Tuesday = new(2026, 10, 6);

    private static DateTimeOffset At(DateOnly date, string time) => CampusTime.At(date, TimeOnly.Parse(time));

    private async Task<(AppDbContext Db, QuotationCalculator Calculator)> CreateAsync()
    {
        var db = PostgresFixture.CreateDbContext(await fixture.CreateDatabaseAsync());
        await Seed.SeedAsync(db, "Test#Password1");
        var clock = new FixedTimeProvider(At(new DateOnly(2026, 9, 27), "10:00"));
        var calculator = new QuotationCalculator(db, new PricingRuleService(db, clock),
            new PolicySettingsService(db, new FakeCurrentUser(null)), new BookingWindowRules(clock));
        return (db, calculator);
    }

    private static Task<long> RoomIdAsync(AppDbContext db, string code) => db.Rooms.Where(r => r.Code == code).Select(r => r.Id).SingleAsync();

    private static Task<long> TypeIdAsync(AppDbContext db, string code) =>
        db.EquipmentTypes.Where(t => t.Code == code).Select(t => t.Id).SingleAsync();

    private static QuoteInput Input(long roomId, DateTimeOffset start, DateTimeOffset end, string role, params QuoteEquipmentLine[] equipment) =>
        new(roomId, start, end, role, equipment);

    private static async Task<IReadOnlyDictionary<string, string[]>> ShouldFailAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Errors;

    [Fact]
    public async Task Demo_quote_for_a_student_is_5500()
    {
        var (db, calculator) = await CreateAsync();
        await using var _ = db;
        var mic = await TypeIdAsync(db, "MIC-WIRELESS");

        var quote = (await calculator.CalculateAsync(Input(await RoomIdAsync(db, "A301"), At(Tuesday, "14:00"), At(Tuesday, "17:00"),
            RequesterRoles.Student, new QuoteEquipmentLine(mic, 2))))!;

        quote.Lines.Should().BeEquivalentTo(new[]
        {
            new QuoteLine(QuotationLineKinds.Room, null, "Computer lab A301, 3 h @ LKR 1,500", 3m, 1500m, 4500m),
            new QuoteLine(QuotationLineKinds.Equipment, mic, "Wireless microphone x2 @ LKR 500", 2m, 500m, 1000m),
        }, o => o.WithStrictOrdering());
        quote.Subtotal.Should().Be(5500.00m);
        quote.Discount.Should().Be(0m);
        quote.DiscountReason.Should().BeNull();
        quote.Exempt.Should().BeFalse();
        quote.Total.Should().Be(5500.00m);
    }

    [Fact]
    public async Task Lecturer_lines_are_priced_normally_and_the_whole_quote_is_discounted()
    {
        var (db, calculator) = await CreateAsync();
        await using var _ = db;
        var mic = await TypeIdAsync(db, "MIC-WIRELESS");

        var quote = (await calculator.CalculateAsync(Input(await RoomIdAsync(db, "A301"), At(Tuesday, "14:00"), At(Tuesday, "17:00"),
            RequesterRoles.Lecturer, new QuoteEquipmentLine(mic, 2))))!;

        // The seeded lecturer rule is exempt, and CK_PricingRules_Exempt_ZeroRate makes its rate 0.
        quote.Lines.Should().BeEquivalentTo(new[]
        {
            new QuoteLine(QuotationLineKinds.Room, null, "Computer lab A301, 3 h @ LKR 0", 3m, 0m, 0m),
            new QuoteLine(QuotationLineKinds.Equipment, mic, "Wireless microphone x2 @ LKR 500", 2m, 500m, 1000m),
        }, o => o.WithStrictOrdering());
        quote.Subtotal.Should().Be(1000.00m);
        quote.Discount.Should().Be(quote.Subtotal);
        quote.DiscountReason.Should().Be(QuotationCalculator.LecturerExemptionReason);
        quote.Exempt.Should().BeTrue();
        quote.Total.Should().Be(0.00m);
    }

    [Fact]
    public async Task Ninety_minutes_is_one_and_a_half_hours_and_line_totals_round_half_away_from_zero()
    {
        var (db, calculator) = await CreateAsync();
        await using var _ = db;
        await PricingTestData.InsertAsync(db, RoomTypes.SeminarRoom, RequesterRoles.Student, new DateOnly(2026, 10, 1), 333.33m);

        var quote = (await calculator.CalculateAsync(Input(await RoomIdAsync(db, "A201"), At(Tuesday, "10:00"), At(Tuesday, "11:30"),
            RequesterRoles.Student)))!;

        // 1.5 × 333.33 = 499.995 → 500.00.
        quote.Lines.Should().ContainSingle().Which.Should().Match<QuoteLine>(l =>
            l.Qty == 1.5m && l.UnitPrice == 333.33m && l.LineTotal == 500.00m && l.Description == "Seminar room A201, 1.5 h @ LKR 333.33");
        quote.Total.Should().Be(500.00m);
    }

    [Fact]
    public async Task A_future_rule_applies_only_from_its_campus_start_date()
    {
        var (db, calculator) = await CreateAsync();
        await using var _ = db;
        await PricingTestData.InsertAsync(db, RoomTypes.ComputerLab, RequesterRoles.Student, new DateOnly(2026, 10, 7), 1800m);
        var a301 = await RoomIdAsync(db, "A301");
        var wednesday = new DateOnly(2026, 10, 7);

        (await calculator.CalculateAsync(Input(a301, At(Tuesday, "14:00"), At(Tuesday, "15:00"), RequesterRoles.Student)))!
            .Total.Should().Be(1500m);
        (await calculator.CalculateAsync(Input(a301, At(wednesday, "08:00"), At(wednesday, "09:00"), RequesterRoles.Student)))!
            .Total.Should().Be(1800m);
    }

    [Fact]
    public async Task No_effective_rule_is_a_400_on_room()
    {
        var (db, calculator) = await CreateAsync();
        await using var _ = db;
        // Wednesday 2025-12-31 is before the seeded rules (2026-01-01).
        var day = new DateOnly(2025, 12, 31);
        var a301 = await RoomIdAsync(db, "A301");

        var errors = await ShouldFailAsync(() => calculator.CalculateAsync(
            Input(a301, At(day, "10:00"), At(day, "11:00"), RequesterRoles.Student)));

        errors.Should().ContainKey("Room").WhoseValue.Single().Should().Contain("No pricing rule");
    }

    [Fact]
    public async Task Equipment_lines_skip_zero_sum_repeats_and_reject_unknown_or_negative()
    {
        var (db, calculator) = await CreateAsync();
        await using var _ = db;
        var a301 = await RoomIdAsync(db, "A301");
        var mic = await TypeIdAsync(db, "MIC-WIRELESS");
        var projector = await TypeIdAsync(db, "PROJ-PORTABLE");
        var (start, end) = (At(Tuesday, "14:00"), At(Tuesday, "15:00"));

        var quote = (await calculator.CalculateAsync(Input(a301, start, end, RequesterRoles.Student,
            new QuoteEquipmentLine(mic, 1), new QuoteEquipmentLine(projector, 0), new QuoteEquipmentLine(mic, 2))))!;
        quote.Lines.Should().HaveCount(2);
        quote.Lines[1].Should().Match<QuoteLine>(l => l.EquipmentTypeId == mic && l.Qty == 3m && l.LineTotal == 1500m);

        (await ShouldFailAsync(() => calculator.CalculateAsync(Input(a301, start, end, RequesterRoles.Student,
            new QuoteEquipmentLine(long.MaxValue, 1))))).Should().ContainKey("Equipment");
        (await ShouldFailAsync(() => calculator.CalculateAsync(Input(a301, start, end, RequesterRoles.Student,
            new QuoteEquipmentLine(mic, -1))))).Should().ContainKey("Equipment");
    }

    [Fact]
    public async Task Impossible_slots_are_400_on_start()
    {
        var (db, calculator) = await CreateAsync();
        await using var _ = db;
        var a301 = await RoomIdAsync(db, "A301");
        var sunday = new DateOnly(2026, 10, 11);

        (await ShouldFailAsync(() => calculator.CalculateAsync(Input(a301, At(sunday, "10:00"), At(sunday, "12:00"), RequesterRoles.Student))))
            .Should().ContainKey("Start").WhoseValue.Single().Should().Be("The campus is closed on Sundays");
        (await ShouldFailAsync(() => calculator.CalculateAsync(Input(a301, At(Tuesday, "14:15"), At(Tuesday, "16:00"), RequesterRoles.Student))))
            .Should().ContainKey("Start").WhoseValue.Single().Should().Be("Must be on a 30-minute boundary");
    }

    [Fact]
    public async Task Inactive_or_unknown_room_returns_null()
    {
        var (db, calculator) = await CreateAsync();
        await using var _ = db;
        var (start, end) = (At(Tuesday, "14:00"), At(Tuesday, "15:00"));

        (await calculator.CalculateAsync(Input(await RoomIdAsync(db, "E305"), start, end, RequesterRoles.Student))).Should().BeNull();
        (await calculator.CalculateAsync(Input(long.MaxValue, start, end, RequesterRoles.Student))).Should().BeNull();
    }
}
