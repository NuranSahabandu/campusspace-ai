using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class RoomsEndpointsTests(PostgresFixture fixture)
{
    private async Task<HttpClient> OfficerAsync() =>
        (await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer)).Client;

    private HttpClient Student() => TestAuth.CreateClient(fixture.Factory, Roles.Student);

    /// <summary>A building of its own holding copies of the seed's A301, A305 and N201, plus an inactive room.</summary>
    private async Task<(long BuildingId, string Prefix)> CreateLabsAsync()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(fixture.Factory);
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(fixture.Factory, prefix);
        await FacilitiesTestData.CreateRoomAsync(fixture.Factory, buildingId, $"{prefix}A301", RoomTypes.ComputerLab, 48,
            ["computers", "projector", "ac", "whiteboard"]);
        await FacilitiesTestData.CreateRoomAsync(fixture.Factory, buildingId, $"{prefix}A305", RoomTypes.ComputerLab, 50,
            ["computers", "whiteboard", "ac"]);
        await FacilitiesTestData.CreateRoomAsync(fixture.Factory, buildingId, $"{prefix}N201", RoomTypes.ComputerLab, 60,
            ["computers", "projector", "ac", "smart_board"]);
        await FacilitiesTestData.CreateRoomAsync(fixture.Factory, buildingId, $"{prefix}OLD", RoomTypes.SeminarRoom, 20,
            ["whiteboard"], isActive: false);
        return (buildingId, prefix);
    }

    private static async Task<string[]> CodesAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .Select(r => r.GetProperty("code").GetString()!).ToArray();
    }

    private static object NewRoom(long buildingId, string code, string[] featureCodes) => new
    {
        code, name = "New Lab", type = RoomTypes.ComputerLab, capacity = 40, buildingId, featureCodes,
    };

    [Fact]
    public async Task Features_filter_returns_only_rooms_that_have_all_of_them()
    {
        var (buildingId, p) = await CreateLabsAsync();

        var codes = await CodesAsync(await Student().GetAsync($"/api/rooms?buildingId={buildingId}&features=computers, Projector"));

        codes.Should().Equal($"{p}A301", $"{p}N201");
    }

    [Fact]
    public async Task Min_capacity_and_building_filters_apply()
    {
        var (buildingId, p) = await CreateLabsAsync();
        await CreateLabsAsync(); // A second building with the same room shapes, which the buildingId filter must exclude.

        var big = await CodesAsync(await Student().GetAsync($"/api/rooms?buildingId={buildingId}&minCapacity=50&sort=-capacity"));
        var all = await CodesAsync(await Student().GetAsync($"/api/rooms?buildingId={buildingId}&pageSize=100"));

        big.Should().Equal($"{p}N201", $"{p}A305");
        all.Should().HaveCount(3).And.OnlyContain(c => c.StartsWith(p));
    }

    [Fact]
    public async Task Inactive_rooms_are_hidden_from_students_even_with_include_inactive_but_officers_can_see_them()
    {
        var (buildingId, p) = await CreateLabsAsync();
        var officer = await OfficerAsync();

        var student = await CodesAsync(await Student().GetAsync($"/api/rooms?buildingId={buildingId}&includeInactive=true"));
        var officerDefault = await CodesAsync(await officer.GetAsync($"/api/rooms?buildingId={buildingId}"));
        var officerAll = await CodesAsync(await officer.GetAsync($"/api/rooms?buildingId={buildingId}&includeInactive=true"));

        student.Should().NotContain($"{p}OLD");
        officerDefault.Should().NotContain($"{p}OLD");
        officerAll.Should().Contain($"{p}OLD");
    }

    [Fact]
    public async Task Room_dto_includes_building_and_sorted_features()
    {
        var (buildingId, p) = await CreateLabsAsync();

        var response = await Student().GetAsync($"/api/rooms?buildingId={buildingId}&search={p}A301");

        var room = (await response.ReadJsonAsync()).GetProperty("items")[0];
        room.GetProperty("type").GetString().Should().Be(RoomTypes.ComputerLab);
        room.GetProperty("capacity").GetInt32().Should().Be(48);
        room.GetProperty("isActive").GetBoolean().Should().BeTrue();
        room.GetProperty("building").GetProperty("id").GetInt64().Should().Be(buildingId);
        room.GetProperty("building").GetProperty("code").GetString().Should().Be(p);
        room.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("code").GetString())
            .Should().Equal("ac", "computers", "projector", "whiteboard");
    }

    [Fact]
    public async Task Invalid_sort_or_type_returns_400()
    {
        (await Student().GetAsync("/api/rooms?sort=price")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Student().GetAsync("/api/rooms?type=Kitchen")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Officer_creates_a_room_with_feature_codes_and_gets_201_with_location()
    {
        var (buildingId, p) = await CreateLabsAsync();
        var officer = await OfficerAsync();

        var response = await officer.PostAsJsonAsync("/api/rooms", NewRoom(buildingId, $" {p}B1 ", [" Projector", "computers"]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var room = await response.ReadJsonAsync();
        room.GetProperty("code").GetString().Should().Be($"{p}B1");
        room.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("code").GetString())
            .Should().Equal("computers", "projector");
        response.Headers.Location!.AbsolutePath.Should().Be($"/api/rooms/{room.GetProperty("id").GetInt64()}");
    }

    [Fact]
    public async Task Unknown_feature_code_returns_400_on_feature_codes()
    {
        var (buildingId, p) = await CreateLabsAsync();
        var officer = await OfficerAsync();

        var response = await officer.PostAsJsonAsync("/api/rooms", NewRoom(buildingId, $"{p}B2", ["projector", "hologram"]));

        var body = await response.ShouldBeProblemAsync(400);
        body.GetProperty("errors").GetProperty("FeatureCodes")[0].GetString().Should().Contain("hologram");
    }

    [Fact]
    public async Task Duplicate_room_code_in_other_casing_returns_409()
    {
        var (buildingId, p) = await CreateLabsAsync();
        var officer = await OfficerAsync();

        var response = await officer.PostAsJsonAsync("/api/rooms", NewRoom(buildingId, $"{p}a301".ToLowerInvariant(), []));

        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("Room code is already taken");
    }

    [Fact]
    public async Task Student_cannot_create_a_room()
    {
        var response = await Student().PostAsJsonAsync("/api/rooms", NewRoom(1, "X1", []));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_replaces_the_feature_set()
    {
        var (buildingId, p) = await CreateLabsAsync();
        var officer = await OfficerAsync();
        var created = await (await officer.PostAsJsonAsync("/api/rooms", NewRoom(buildingId, $"{p}B3", ["projector", "ac"]))).ReadJsonAsync();
        var id = created.GetProperty("id").GetInt64();

        var response = await officer.PutAsJsonAsync($"/api/rooms/{id}", new
        {
            code = $"{p}B3", name = "Renamed", type = RoomTypes.SeminarRoom, capacity = 25, buildingId,
            featureCodes = new[] { "ac", "whiteboard" }, isActive = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var room = await response.ReadJsonAsync();
        room.GetProperty("name").GetString().Should().Be("Renamed");
        room.GetProperty("type").GetString().Should().Be(RoomTypes.SeminarRoom);
        room.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("code").GetString()).Should().Equal("ac", "whiteboard");
    }

    [Fact]
    public async Task Delete_deactivates_the_room_so_students_no_longer_see_it()
    {
        var (buildingId, p) = await CreateLabsAsync();
        var officer = await OfficerAsync();
        var id = (await (await officer.PostAsJsonAsync("/api/rooms", NewRoom(buildingId, $"{p}B4", []))).ReadJsonAsync())
            .GetProperty("id").GetInt64();

        var delete = await officer.DeleteAsync($"/api/rooms/{id}");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Student().GetAsync($"/api/rooms/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodesAsync(await Student().GetAsync($"/api/rooms?buildingId={buildingId}"))).Should().NotContain($"{p}B4");
        var asOfficer = await officer.GetAsync($"/api/rooms/{id}");
        asOfficer.StatusCode.Should().Be(HttpStatusCode.OK);
        (await asOfficer.ReadJsonAsync()).GetProperty("isActive").GetBoolean().Should().BeFalse();
    }
}
