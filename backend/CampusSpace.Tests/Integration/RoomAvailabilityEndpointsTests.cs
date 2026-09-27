using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// GET /api/rooms/availability. Each test puts its rooms in a building of its own and filters by it. Availability
/// ignores lead time and the advance window, so fixed dates in 2031 work whenever the tests run.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RoomAvailabilityEndpointsTests(PostgresFixture fixture)
{
    private static readonly DateOnly Monday = new(2031, 3, 10);
    private static readonly DateOnly Sunday = new(2031, 3, 16);

    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static string Url(
        DateTimeOffset start, DateTimeOffset end, int minCapacity = 1, string? features = null, long? buildingId = null,
        string? type = null, int? maxCapacity = null, string? extra = null)
    {
        var query = $"/api/rooms/availability?start={Uri.EscapeDataString(start.ToString("O"))}" +
                    $"&end={Uri.EscapeDataString(end.ToString("O"))}&minCapacity={minCapacity}";
        if (features is not null) query += $"&features={Uri.EscapeDataString(features)}";
        if (buildingId is not null) query += $"&buildingId={buildingId}";
        if (type is not null) query += $"&type={type}";
        if (maxCapacity is not null) query += $"&maxCapacity={maxCapacity}";
        return query + extra;
    }

    private static string AfternoonUrl(long buildingId, int minCapacity = 1, string? features = null, string? type = null, int? maxCapacity = null)
    {
        var (start, end) = CampusSlot(Monday, "14:00", "17:00");
        return Url(start, end, minCapacity, features, buildingId, type, maxCapacity);
    }

    private async Task<List<string>> CodesAsync(string url)
    {
        var body = await (await TestAuth.CreateClient(Factory, Roles.Student).GetAsync(url)).ReadJsonAsync();
        return body.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("code").GetString()!).ToList();
    }

    private async Task<(string Prefix, long BuildingId)> BuildingAsync()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(Factory);
        var prefix = FacilitiesTestData.UniquePrefix();
        return (prefix, await FacilitiesTestData.CreateBuildingAsync(Factory, prefix));
    }

    [Fact]
    public async Task Filters_by_capacity_range_all_features_and_type_and_orders_by_best_fit()
    {
        var (p, building) = await BuildingAsync();
        async Task Room(string code, string type, int capacity, string[] features, bool active = true) =>
            await FacilitiesTestData.CreateRoomAsync(Factory, building, $"{p}-{code}", type, capacity, features, active);
        await Room("A", RoomTypes.SeminarRoom, 30, ["projector"]);
        await Room("C", RoomTypes.ComputerLab, 50, ["projector", "computers"]);
        await Room("B", RoomTypes.LectureHall, 50, ["projector", "computers", "ac"]);
        await Room("D", RoomTypes.ComputerLab, 80, ["computers"]);
        await Room("E", RoomTypes.ComputerLab, 60, ["projector", "computers"], active: false);

        (await CodesAsync(AfternoonUrl(building))).Should().Equal($"{p}-A", $"{p}-B", $"{p}-C", $"{p}-D");
        (await CodesAsync(AfternoonUrl(building, minCapacity: 40))).Should().Equal($"{p}-B", $"{p}-C", $"{p}-D");
        (await CodesAsync(AfternoonUrl(building, minCapacity: 40, maxCapacity: 60))).Should().Equal($"{p}-B", $"{p}-C");
        (await CodesAsync(AfternoonUrl(building, features: " Projector ,computers"))).Should().Equal($"{p}-B", $"{p}-C");
        (await CodesAsync(AfternoonUrl(building, features: "projector,computers,ac"))).Should().Equal($"{p}-B");
        (await CodesAsync(AfternoonUrl(building, type: RoomTypes.ComputerLab))).Should().Equal($"{p}-C", $"{p}-D");
    }

    [Fact]
    public async Task Blackouts_and_active_bookings_hide_a_room_but_cancelled_and_adjacent_ones_do_not()
    {
        var (p, building) = await BuildingAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);
        var rooms = new Dictionary<string, long>();
        foreach (var name in new[] { "BLACKOUT", "CONFIRMED", "CHECKEDIN", "CANCELLED", "ADJACENT", "FREE" })
            rooms[name] = await FacilitiesTestData.CreateRoomAsync(Factory, building, $"{p}-{name}", RoomTypes.SeminarRoom, 20, []);

        async Task BlackoutAsync(long roomId, string from, string to)
        {
            var (start, end) = CampusSlot(Monday, from, to);
            (await officer.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", new { start, end, reason = "Maintenance" }))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }
        async Task BookAsync(long roomId, string from, string to, string status = BookingStatuses.Confirmed)
        {
            var (start, end) = CampusSlot(Monday, from, to);
            await InsertBookingAsync(Factory, roomId, start, end, status);
        }

        await BlackoutAsync(rooms["BLACKOUT"], "16:30", "18:00");
        await BookAsync(rooms["CONFIRMED"], "13:00", "14:30");
        await BookAsync(rooms["CHECKEDIN"], "15:00", "16:00", BookingStatuses.CheckedIn);
        await BookAsync(rooms["CANCELLED"], "14:00", "17:00", BookingStatuses.Cancelled);
        await BookAsync(rooms["CANCELLED"], "08:00", "10:00", BookingStatuses.Completed);
        // [12:00, 14:00) and [17:00, 18:00) touch 14:00–17:00 without overlapping it.
        await BookAsync(rooms["ADJACENT"], "12:00", "14:00");
        await BlackoutAsync(rooms["ADJACENT"], "17:00", "18:00");

        (await CodesAsync(AfternoonUrl(building))).Should().Equal($"{p}-ADJACENT", $"{p}-CANCELLED", $"{p}-FREE");
    }

    [Fact]
    public async Task The_plans_eval_case_finds_A301_and_N201_on_the_seeded_data()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
            await Seed.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "demo-password-1");

        // 45 people who need computers and a projector: A305 has no projector, E201 seats 40, so A301 (48) then N201 (60).
        var (start, end) = CampusSlot(Monday, "14:00", "17:00");
        var body = await (await TestAuth.CreateClient(factory, Roles.Lecturer)
            .GetAsync(Url(start, end, minCapacity: 45, features: "computers,projector"))).ReadJsonAsync();

        body.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Equal("A301", "N201");
        body.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Rows_have_the_room_list_shape_and_are_paged()
    {
        var (p, building) = await BuildingAsync();
        await FacilitiesTestData.CreateRoomAsync(Factory, building, $"{p}-1", RoomTypes.SeminarRoom, 10, ["whiteboard", "ac"]);
        await FacilitiesTestData.CreateRoomAsync(Factory, building, $"{p}-2", RoomTypes.SeminarRoom, 20, []);

        var body = await (await TestAuth.CreateClient(Factory, Roles.Student).GetAsync(AfternoonUrl(building) + "&page=2&pageSize=1"))
            .ReadJsonAsync();

        body.GetProperty("total").GetInt32().Should().Be(2);
        body.GetProperty("page").GetInt32().Should().Be(2);
        var row = body.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("code").GetString().Should().Be($"{p}-2");
        row.GetProperty("building").GetProperty("id").GetInt64().Should().Be(building);
        row.GetProperty("features").ValueKind.Should().Be(JsonValueKind.Array);
        row.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Slots_that_break_the_opening_hours_granularity_or_order_are_400()
    {
        var client = TestAuth.CreateClient(Factory, Roles.Student);
        async Task<JsonElement> ErrorsAsync(string url) => (await (await client.GetAsync(url)).ShouldBeProblemAsync(400)).GetProperty("errors");

        var (sunStart, sunEnd) = CampusSlot(Sunday, "14:00", "17:00");
        (await ErrorsAsync(Url(sunStart, sunEnd))).GetProperty("Start")[0].GetString().Should().Be("The campus is closed on Sundays");

        var (offStart, offEnd) = CampusSlot(Monday, "14:15", "17:00");
        (await ErrorsAsync(Url(offStart, offEnd))).GetProperty("Start")[0].GetString().Should().Be("Must be on a 30-minute boundary");

        var (longStart, longEnd) = CampusSlot(Monday, "08:00", "17:00");
        (await ErrorsAsync(Url(longStart, longEnd))).GetProperty("End")[0].GetString().Should().Be("Bookings can be at most 8 hours");

        var (start, end) = CampusSlot(Monday, "14:00", "17:00");
        (await ErrorsAsync(Url(end, start))).GetProperty("End")[0].GetString().Should().Be("End must be after Start.");
    }

    [Fact]
    public async Task Bad_parameters_and_unknown_features_are_400()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(Factory);
        var client = TestAuth.CreateClient(Factory, Roles.Student);
        var (start, end) = CampusSlot(Monday, "14:00", "17:00");
        async Task<JsonElement> ErrorsAsync(string url) => (await (await client.GetAsync(url)).ShouldBeProblemAsync(400)).GetProperty("errors");

        (await ErrorsAsync(Url(start, end, features: "projector,Hologram,jetpack"))).GetProperty("Features")[0].GetString()
            .Should().Be("Unknown feature codes: hologram, jetpack.");
        (await ErrorsAsync(Url(start, end, minCapacity: 50, maxCapacity: 40))).TryGetProperty("MaxCapacity", out _).Should().BeTrue();
        (await ErrorsAsync(Url(start, end, extra: "&sort=capacity"))).TryGetProperty("Sort", out _).Should().BeTrue();
        (await ErrorsAsync(Url(start, end, type: "Cafeteria"))).TryGetProperty("Type", out _).Should().BeTrue();
        (await ErrorsAsync("/api/rooms/availability?minCapacity=10")).TryGetProperty("Start", out _).Should().BeTrue();
        (await ErrorsAsync(Url(start, end, minCapacity: 0))).TryGetProperty("MinCapacity", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Needs_a_signed_in_user_of_any_role_and_is_not_taken_for_a_room_id()
    {
        var (start, end) = CampusSlot(Monday, "14:00", "17:00");
        await (await Factory.CreateClient().GetAsync(Url(start, end))).ShouldBeProblemAsync(401);

        foreach (var role in Roles.All)
            (await TestAuth.CreateClient(Factory, role).GetAsync(Url(start, end))).StatusCode.Should().Be(HttpStatusCode.OK, role);
    }
}
