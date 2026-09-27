using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class RoomBlackoutsEndpointsTests(PostgresFixture fixture)
{
    private async Task<long> RoomAsync()
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(fixture.Factory, prefix);
        return await FacilitiesTestData.CreateRoomAsync(fixture.Factory, buildingId, $"{prefix}L1", RoomTypes.LectureHall, 100, []);
    }

    [Fact]
    public async Task End_not_after_start_returns_400_on_end()
    {
        var (officer, _) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);
        var roomId = await RoomAsync();

        var response = await officer.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", new
        {
            start = "2026-10-05T10:00:00+05:30", end = "2026-10-05T10:00:00+05:30", reason = "Painting",
        });

        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("End", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_stores_a_utc_lower_inclusive_upper_exclusive_range_and_lists_it()
    {
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);
        var roomId = await RoomAsync();

        var response = await officer.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", new
        {
            start = "2026-10-05T08:00:00+05:30", end = "2026-10-05T12:00:00+05:30", reason = " Projector maintenance ",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var blackout = await response.ReadJsonAsync();
        var id = blackout.GetProperty("id").GetInt64();
        response.Headers.Location!.AbsolutePath.Should().Be($"/api/rooms/{roomId}/blackouts/{id}");
        blackout.GetProperty("start").GetDateTime().Should().Be(new DateTime(2026, 10, 5, 2, 30, 0, DateTimeKind.Utc));
        blackout.GetProperty("end").GetDateTime().Should().Be(new DateTime(2026, 10, 5, 6, 30, 0, DateTimeKind.Utc));
        blackout.GetProperty("reason").GetString().Should().Be("Projector maintenance");
        blackout.GetProperty("createdById").GetInt64().Should().Be(officerId);

        var list = await (await officer.GetAsync(
            $"/api/rooms/{roomId}/blackouts?from=2026-10-05T00:00:00Z&to=2026-10-06T00:00:00Z")).ReadJsonAsync();
        list.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetInt64()).Should().Equal(id);
        var outside = await (await officer.GetAsync(
            $"/api/rooms/{roomId}/blackouts?from=2026-10-05T06:30:00Z")).ReadJsonAsync();
        outside.GetProperty("total").GetInt32().Should().Be(0, "the end instant is excluded, so [06:30, ∞) does not overlap");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT lower("TimeRange") = timestamptz '2026-10-05 02:30:00+00', lower_inc("TimeRange"),
                   upper_inc("TimeRange"), "TimeRange" @> timestamptz '2026-10-05 06:30:00+00'
            FROM "RoomBlackouts" WHERE "Id" = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetBoolean(0).Should().BeTrue("the lower bound is the UTC instant");
        reader.GetBoolean(1).Should().BeTrue("the start is included");
        reader.GetBoolean(2).Should().BeFalse("the end is excluded");
        reader.GetBoolean(3).Should().BeFalse("the range does not contain its end instant");
    }

    [Fact]
    public async Task Delete_returns_204_and_a_blackout_of_another_room_returns_404()
    {
        var (officer, _) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);
        var (roomId, otherRoomId) = (await RoomAsync(), await RoomAsync());
        var id = (await (await officer.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", new
        {
            start = "2026-11-01T08:00:00Z", end = "2026-11-01T09:00:00Z", reason = "Cleaning",
        })).ReadJsonAsync()).GetProperty("id").GetInt64();

        (await officer.DeleteAsync($"/api/rooms/{otherRoomId}/blackouts/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await officer.DeleteAsync($"/api/rooms/{roomId}/blackouts/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await officer.GetAsync($"/api/rooms/{roomId}/blackouts/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Students_cannot_read_or_add_blackouts_and_a_missing_room_is_404()
    {
        var student = TestAuth.CreateClient(fixture.Factory, Roles.Student);
        var (officer, _) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);
        var roomId = await RoomAsync();

        (await student.GetAsync($"/api/rooms/{roomId}/blackouts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", new
        {
            start = "2026-11-01T08:00:00Z", end = "2026-11-01T09:00:00Z", reason = "x",
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await officer.GetAsync("/api/rooms/999999999/blackouts")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static readonly DateOnly ClashDay = new(2031, 3, 12);

    private static object BlackoutBody(string from, string to) =>
        new { start = CampusSlot(ClashDay, from, to).Start, end = CampusSlot(ClashDay, from, to).End, reason = "Rewiring" };

    private async Task<(string Name, string Email)> RequesterOfAsync(long bookingId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Bookings.Where(b => b.Id == bookingId).Select(b => b.Request.Requester).SingleAsync();
        return (user.FullName, user.Email);
    }

    private static List<long> BookingIds(JsonElement clashes) =>
        clashes.EnumerateArray().Select(c => c.GetProperty("bookingId").GetInt64()).ToList();

    [Fact]
    public async Task Creating_a_blackout_over_active_bookings_returns_them_as_clashes_and_keeps_them()
    {
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);
        var roomId = await RoomAsync();
        var otherRoomId = await RoomAsync();
        async Task<long> Book(long room, string from, string to, string status = BookingStatuses.Confirmed)
        {
            var (start, end) = CampusSlot(ClashDay, from, to);
            return await InsertBookingAsync(fixture.Factory, room, start, end, status);
        }
        var confirmed = await Book(roomId, "09:00", "11:00");
        var checkedIn = await Book(roomId, "12:00", "14:00", BookingStatuses.CheckedIn);
        await Book(roomId, "10:00", "12:00", BookingStatuses.Cancelled);
        await Book(roomId, "14:00", "16:00");
        await Book(roomId, "08:00", "09:00");
        await Book(otherRoomId, "10:00", "12:00");

        // [10:00, 14:00) overlaps 09:00-11:00 and 12:00-14:00; the adjacent 08:00-09:00 and 14:00-16:00 don't.
        var response = await officer.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", BlackoutBody("10:00", "14:00"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.ReadJsonAsync();
        body.GetProperty("roomId").GetInt64().Should().Be(roomId);
        body.GetProperty("reason").GetString().Should().Be("Rewiring");
        body.GetProperty("createdById").GetInt64().Should().Be(officerId);
        var clashes = body.GetProperty("clashes");
        BookingIds(clashes).Should().Equal(confirmed, checkedIn);

        var first = clashes[0];
        var (name, email) = await RequesterOfAsync(confirmed);
        first.GetProperty("requesterName").GetString().Should().Be(name);
        first.GetProperty("requesterEmail").GetString().Should().Be(email);
        first.GetProperty("status").GetString().Should().Be(BookingStatuses.Confirmed);
        first.GetProperty("requestId").GetInt64().Should().BePositive();
        first.GetProperty("start").GetDateTime().Should().Be(CampusSlot(ClashDay, "09:00", "11:00").Start.UtcDateTime);
        first.GetProperty("end").GetDateTime().Should().Be(CampusSlot(ClashDay, "09:00", "11:00").End.UtcDateTime);
        clashes[1].GetProperty("status").GetString().Should().Be(BookingStatuses.CheckedIn);

        // The clashes endpoint returns the same list; the bookings were not cancelled.
        var blackoutId = body.GetProperty("id").GetInt64();
        var listed = await (await officer.GetAsync($"/api/rooms/{roomId}/blackouts/{blackoutId}/clashes")).ReadJsonAsync();
        listed.GetRawText().Should().Be(clashes.GetRawText());
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Bookings.CountAsync(b => b.RoomId == roomId && BookingStatuses.Active.Contains(b.Status))).Should().Be(4);
    }

    [Fact]
    public async Task A_blackout_on_an_empty_slot_has_no_clashes()
    {
        var (officer, _) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);
        var roomId = await RoomAsync();

        var body = await (await officer.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", BlackoutBody("10:00", "12:00"))).ReadJsonAsync();

        body.GetProperty("clashes").GetArrayLength().Should().Be(0);
        var id = body.GetProperty("id").GetInt64();
        (await (await officer.GetAsync($"/api/rooms/{roomId}/blackouts/{id}/clashes")).ReadJsonAsync()).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Clashes_are_for_officers_only_and_404_for_a_missing_blackout()
    {
        var (officer, _) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);
        var roomId = await RoomAsync();
        var otherRoomId = await RoomAsync();
        var id = (await (await officer.PostAsJsonAsync($"/api/rooms/{roomId}/blackouts", BlackoutBody("10:00", "12:00"))).ReadJsonAsync())
            .GetProperty("id").GetInt64();

        (await TestAuth.CreateClient(fixture.Factory, Roles.Student).GetAsync($"/api/rooms/{roomId}/blackouts/{id}/clashes"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await officer.GetAsync($"/api/rooms/{roomId}/blackouts/999999999/clashes")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await officer.GetAsync($"/api/rooms/{otherRoomId}/blackouts/{id}/clashes")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
