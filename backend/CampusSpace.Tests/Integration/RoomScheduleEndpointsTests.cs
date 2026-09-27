using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>GET /api/rooms/{id}/schedule on fixed 2031 dates (Monday 10 March, Saturday 15, Sunday 16), seeded policy hours.</summary>
[Collection(PostgresCollection.Name)]
public class RoomScheduleEndpointsTests(PostgresFixture fixture)
{
    private static readonly DateOnly Monday = new(2031, 3, 10);

    private CustomWebApplicationFactory Factory => fixture.Factory;

    private async Task<long> RoomAsync(bool isActive = true)
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(Factory, prefix);
        return await FacilitiesTestData.CreateRoomAsync(Factory, buildingId, prefix + "-R", RoomTypes.SeminarRoom, 30, [], isActive);
    }

    private static string Url(long roomId, string date) => $"/api/rooms/{roomId}/schedule?date={date}";

    private static DateTimeOffset At(string time, int dayOffset = 0) => CampusTime.At(Monday.AddDays(dayOffset), TimeOnly.Parse(time));

    private static List<(DateTimeOffset Start, DateTimeOffset End, string? Kind, string? Label)> Intervals(JsonElement list) =>
        list.EnumerateArray().Select(i => (
            i.GetProperty("start").GetDateTimeOffset(), i.GetProperty("end").GetDateTimeOffset(),
            i.TryGetProperty("kind", out var k) ? k.GetString() : null,
            i.TryGetProperty("label", out var l) ? l.GetString() : null)).ToList();

    [Fact]
    public async Task A_day_with_bookings_and_blackouts_shows_busy_clipped_and_free_snapped_time()
    {
        var roomId = await RoomAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);
        async Task BlackoutAsync(DateTimeOffset start, DateTimeOffset end, string reason) =>
            (await officer.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", new { start, end, reason }))
                .StatusCode.Should().Be(HttpStatusCode.Created);

        await BlackoutAsync(At("18:00", dayOffset: -1), At("09:00"), "Carpet replacement");
        await InsertBookingAsync(Factory, roomId, At("10:00"), At("12:00"));
        await InsertBookingAsync(Factory, roomId, At("13:00"), At("14:00"), BookingStatuses.Cancelled);
        await BlackoutAsync(At("15:10"), At("16:00"), "Painting");
        await InsertBookingAsync(Factory, roomId, At("10:00", dayOffset: 1), At("12:00", dayOffset: 1));

        var response = await TestAuth.CreateClient(Factory, Roles.Student).GetAsync(Url(roomId, "2031-03-10"));
        var body = await response.ReadJsonAsync();

        body.GetProperty("date").GetString().Should().Be("2031-03-10");
        body.GetProperty("open").GetString().Should().Be("08:00");
        body.GetProperty("close").GetString().Should().Be("20:00");
        body.GetProperty("granularityMinutes").GetInt32().Should().Be(30);
        Intervals(body.GetProperty("busy")).Should().Equal(
            (At("00:00"), At("09:00"), "Blackout", "Carpet replacement"),
            (At("10:00"), At("12:00"), "Booking", "Booked"),
            (At("15:10"), At("16:00"), "Blackout", "Painting"));
        // 15:00–15:10 is less than a 30-minute slot, so the gap before Painting ends at 15:00.
        Intervals(body.GetProperty("free")).Should().Equal(
            (At("09:00"), At("10:00"), null, null),
            (At("12:00"), At("15:00"), null, null),
            (At("16:00"), At("20:00"), null, null));
        body.GetProperty("busy")[0].GetProperty("start").GetString().Should().EndWith("Z", "times are UTC");
    }

    [Fact]
    public async Task A_booking_shows_only_booked_never_the_requester_or_purpose()
    {
        var roomId = await RoomAsync();
        var bookingId = await InsertBookingAsync(Factory, roomId, At("10:00"), At("12:00"));
        await using var scope = Factory.Services.CreateAsyncScope();
        var request = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking()
            .Where(b => b.Id == bookingId).Select(b => new { b.Request.Purpose, b.Request.Requester.FullName, b.Request.Requester.Email })
            .SingleAsync();

        var text = await (await TestAuth.CreateClient(Factory, Roles.Student).GetAsync(Url(roomId, "2031-03-10"))).Content.ReadAsStringAsync();

        text.Should().Contain("\"Booked\"").And.NotContain(request.Purpose).And.NotContain(request.FullName).And.NotContain(request.Email);
    }

    [Fact]
    public async Task Saturday_uses_its_own_closing_time_and_Sunday_is_closed()
    {
        var roomId = await RoomAsync();
        await InsertBookingAsync(Factory, roomId, At("10:00", dayOffset: 6), At("11:00", dayOffset: 6));
        var client = TestAuth.CreateClient(Factory, Roles.Lecturer);

        var saturday = await (await client.GetAsync(Url(roomId, "2031-03-15"))).ReadJsonAsync();
        saturday.GetProperty("close").GetString().Should().Be("16:00");
        Intervals(saturday.GetProperty("free")).Last().End.Should().Be(At("16:00", dayOffset: 5));

        var sunday = await (await client.GetAsync(Url(roomId, "2031-03-16"))).ReadJsonAsync();
        sunday.GetProperty("open").ValueKind.Should().Be(JsonValueKind.Null);
        sunday.GetProperty("close").ValueKind.Should().Be(JsonValueKind.Null);
        sunday.GetProperty("free").GetArrayLength().Should().Be(0);
        Intervals(sunday.GetProperty("busy")).Should().ContainSingle().Which.Kind.Should().Be("Booking");
    }

    [Fact]
    public async Task Unknown_rooms_are_404_and_inactive_rooms_are_404_except_for_officers()
    {
        var inactive = await RoomAsync(isActive: false);

        await (await TestAuth.CreateClient(Factory, Roles.Student).GetAsync(Url(long.MaxValue, "2031-03-10"))).ShouldBeProblemAsync(404);
        await (await TestAuth.CreateClient(Factory, Roles.Student).GetAsync(Url(inactive, "2031-03-10"))).ShouldBeProblemAsync(404);
        (await TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer).GetAsync(Url(inactive, "2031-03-10")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_missing_or_invalid_date_is_400_and_anonymous_is_401()
    {
        var roomId = await RoomAsync();
        var client = TestAuth.CreateClient(Factory, Roles.Student);

        await (await client.GetAsync($"/api/rooms/{roomId}/schedule")).ShouldBeProblemAsync(400);
        await (await client.GetAsync(Url(roomId, "2031-02-30"))).ShouldBeProblemAsync(400);
        await (await client.GetAsync(Url(roomId, "tomorrow"))).ShouldBeProblemAsync(400);
        await (await Factory.CreateClient().GetAsync(Url(roomId, "2031-03-10"))).ShouldBeProblemAsync(401);
    }
}
