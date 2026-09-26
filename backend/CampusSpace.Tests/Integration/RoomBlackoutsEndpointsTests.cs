using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;

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
}
