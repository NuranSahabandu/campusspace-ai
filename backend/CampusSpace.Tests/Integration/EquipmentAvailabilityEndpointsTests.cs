using System.Net;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// GET /api/equipment/availability. Each test makes its own equipment type, so other tests' reservations never count.
/// Every booking gets a room of its own, so overlapping bookings don't trip no_room_overlap. Availability ignores lead
/// time and the advance window, so fixed dates in 2031 work whenever the tests run.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EquipmentAvailabilityEndpointsTests(PostgresFixture fixture)
{
    private static readonly DateOnly Monday = new(2031, 3, 10);
    private static readonly DateOnly Sunday = new(2031, 3, 16);

    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static string Url(long typeId, DateTimeOffset start, DateTimeOffset end) =>
        $"/api/equipment/availability?typeId={typeId}&start={Uri.EscapeDataString(start.ToString("O"))}" +
        $"&end={Uri.EscapeDataString(end.ToString("O"))}";

    private static string Url(long typeId, string from = "14:00", string to = "17:00")
    {
        var (start, end) = CampusSlot(Monday, from, to);
        return Url(typeId, start, end);
    }

    private async Task<JsonElement> GetAsync(string url, CustomWebApplicationFactory? factory = null)
    {
        var response = await TestAuth.CreateClient(factory ?? Factory, Roles.Student).GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ReadJsonAsync();
    }

    /// <summary>A type with <paramref name="available"/> Available items.</summary>
    private async Task<long> TypeAsync(int available)
    {
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        for (var i = 0; i < available; i++)
            await EquipmentTestData.CreateItemAsync(Factory, type.Id);
        return type.Id;
    }

    /// <summary>A booking of a new room on Monday [from, to) that reserves <paramref name="quantity"/> of the type.</summary>
    private async Task ReserveAsync(long typeId, int quantity, string from, string to, string status = BookingStatuses.Confirmed)
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(Factory, prefix);
        var roomId = await FacilitiesTestData.CreateRoomAsync(Factory, buildingId, prefix + "-R", RoomTypes.SeminarRoom, 30, []);
        var (start, end) = CampusSlot(Monday, from, to);
        var bookingId = await InsertBookingAsync(Factory, roomId, start, end, status);
        await EquipmentTestData.InsertReservationAsync(Factory, bookingId, typeId, quantity);
    }

    private static void ShouldHave(JsonElement body, int serviceable, int reserved, int available, bool overAllocated = false)
    {
        body.GetProperty("serviceable").GetInt32().Should().Be(serviceable);
        body.GetProperty("reserved").GetInt32().Should().Be(reserved);
        body.GetProperty("available").GetInt32().Should().Be(available);
        body.GetProperty("overAllocated").GetBoolean().Should().Be(overAllocated);
    }

    [Fact]
    public async Task Seeded_wireless_mics_exclude_the_one_under_repair_and_laptops_exclude_the_retired_one()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        Dictionary<string, long> types;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Seed.SeedAsync(db, "demo-password-1");
            types = await db.EquipmentTypes.ToDictionaryAsync(t => t.Code, t => t.Id);
        }

        var mics = await GetAsync(Url(types["MIC-WIRELESS"]), factory);
        mics.GetProperty("typeId").GetInt64().Should().Be(types["MIC-WIRELESS"]);
        mics.GetProperty("code").GetString().Should().Be("MIC-WIRELESS");
        mics.GetProperty("name").GetString().Should().Be("Wireless microphone");
        ShouldHave(mics, serviceable: 7, reserved: 0, available: 7);

        ShouldHave(await GetAsync(Url(types["LAPTOP"]), factory), serviceable: 7, reserved: 0, available: 7);
    }

    [Fact]
    public async Task On_loan_items_count_as_serviceable_but_under_repair_and_retired_ones_do_not()
    {
        var typeId = await TypeAsync(available: 2);
        await EquipmentTestData.CreateItemAsync(Factory, typeId, EquipmentItemStatuses.OnLoan);
        await EquipmentTestData.CreateItemAsync(Factory, typeId, EquipmentItemStatuses.UnderRepair, EquipmentConditions.Damaged);
        await EquipmentTestData.CreateItemAsync(Factory, typeId, EquipmentItemStatuses.Retired);

        ShouldHave(await GetAsync(Url(typeId)), serviceable: 3, reserved: 0, available: 3);
    }

    [Fact]
    public async Task Active_overlapping_reservations_are_subtracted_and_add_up()
    {
        var typeId = await TypeAsync(available: 7);
        await ReserveAsync(typeId, 2, "13:00", "15:00");
        ShouldHave(await GetAsync(Url(typeId)), serviceable: 7, reserved: 2, available: 5);

        await ReserveAsync(typeId, 1, "16:00", "18:00", BookingStatuses.CheckedIn);
        ShouldHave(await GetAsync(Url(typeId)), serviceable: 7, reserved: 3, available: 4);
    }

    [Fact]
    public async Task Cancelled_completed_and_adjacent_reservations_do_not_count()
    {
        var typeId = await TypeAsync(available: 7);
        await ReserveAsync(typeId, 2, "14:00", "17:00", BookingStatuses.Cancelled);
        await ReserveAsync(typeId, 2, "14:00", "17:00", BookingStatuses.Completed);
        await ReserveAsync(typeId, 3, "12:00", "14:00");
        await ReserveAsync(typeId, 3, "17:00", "19:00");

        ShouldHave(await GetAsync(Url(typeId)), serviceable: 7, reserved: 0, available: 7);
    }

    [Fact]
    public async Task More_reserved_than_serviceable_is_flagged_and_available_stays_at_zero()
    {
        var typeId = await TypeAsync(available: 3);
        await ReserveAsync(typeId, 3, "14:00", "16:00");
        // An item breaks after the reservation was made.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.EquipmentItems.FirstAsync(i => i.TypeId == typeId && i.Status == EquipmentItemStatuses.Available);
            item.Status = EquipmentItemStatuses.UnderRepair;
            await db.SaveChangesAsync();
        }

        ShouldHave(await GetAsync(Url(typeId)), serviceable: 2, reserved: 3, available: 0, overAllocated: true);
    }

    [Fact]
    public async Task Slots_that_break_the_opening_hours_granularity_or_order_are_400()
    {
        var typeId = await TypeAsync(available: 1);
        var client = TestAuth.CreateClient(Factory, Roles.Student);
        async Task<JsonElement> ErrorsAsync(string url) => (await (await client.GetAsync(url)).ShouldBeProblemAsync(400)).GetProperty("errors");

        var (sunStart, sunEnd) = CampusSlot(Sunday, "14:00", "17:00");
        (await ErrorsAsync(Url(typeId, sunStart, sunEnd))).GetProperty("Start")[0].GetString().Should().Be("The campus is closed on Sundays");

        (await ErrorsAsync(Url(typeId, "14:15", "17:00"))).GetProperty("Start")[0].GetString().Should().Be("Must be on a 30-minute boundary");

        var (start, end) = CampusSlot(Monday, "14:00", "17:00");
        (await ErrorsAsync(Url(typeId, end, start))).GetProperty("End")[0].GetString().Should().Be("End must be after Start.");
        (await ErrorsAsync(Url(typeId, start, start))).GetProperty("End")[0].GetString().Should().Be("End must be after Start.");
        (await ErrorsAsync("/api/equipment/availability?typeId=1")).TryGetProperty("Start", out _).Should().BeTrue();
        (await ErrorsAsync(Url(0, start, end))).TryGetProperty("TypeId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_type_is_404()
    {
        await (await TestAuth.CreateClient(Factory, Roles.Student).GetAsync(Url(long.MaxValue))).ShouldBeProblemAsync(404);
    }

    [Fact]
    public async Task Needs_a_signed_in_user_of_any_role()
    {
        var typeId = await TypeAsync(available: 1);
        await (await Factory.CreateClient().GetAsync(Url(typeId))).ShouldBeProblemAsync(401);

        foreach (var role in Roles.All)
            (await TestAuth.CreateClient(Factory, role).GetAsync(Url(typeId))).StatusCode.Should().Be(HttpStatusCode.OK, role);
    }
}
