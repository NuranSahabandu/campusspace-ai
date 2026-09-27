using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class BuildingsFeaturesEndpointsTests(PostgresFixture fixture)
{
    private async Task<HttpClient> OfficerAsync() =>
        (await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer)).Client;

    private HttpClient Student() => TestAuth.CreateClient(fixture.Factory, Roles.Student);

    private static string UniqueFeatureCode() => "f_" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Officer_creates_a_building_with_a_normalized_code_and_a_student_can_list_it()
    {
        var officer = await OfficerAsync();
        var code = FacilitiesTestData.UniquePrefix();

        var created = await officer.PostAsJsonAsync("/api/buildings", new { code = $" {code.ToLowerInvariant()} ", name = " Science Block " });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.ReadJsonAsync();
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("name").GetString().Should().Be("Science Block");
        created.Headers.Location!.AbsolutePath.Should().Be($"/api/buildings/{body.GetProperty("id").GetInt64()}");

        var list = await Student().GetAsync("/api/buildings");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.ReadJsonAsync()).EnumerateArray().Select(b => b.GetProperty("code").GetString()).Should().Contain(code);
    }

    [Fact]
    public async Task Duplicate_building_code_in_other_casing_returns_409_and_a_bad_code_returns_400()
    {
        var officer = await OfficerAsync();
        var code = FacilitiesTestData.UniquePrefix();
        (await officer.PostAsJsonAsync("/api/buildings", new { code, name = "A" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await officer.PostAsJsonAsync("/api/buildings", new { code = code.ToLowerInvariant(), name = "B" });
        var invalid = await officer.PostAsJsonAsync("/api/buildings", new { code = "M B", name = "C" });

        (await duplicate.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("Building code is already taken");
        (await invalid.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("Code", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Officer_creates_a_feature_with_a_lower_cased_code_and_a_student_can_list_it()
    {
        var officer = await OfficerAsync();
        var code = UniqueFeatureCode();

        var created = await officer.PostAsJsonAsync("/api/features", new { code = $" {code.ToUpperInvariant()} ", name = "Smart TV" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await created.ReadJsonAsync()).GetProperty("code").GetString().Should().Be(code);
        var list = await Student().GetAsync("/api/features");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.ReadJsonAsync()).EnumerateArray().Select(f => f.GetProperty("code").GetString()).Should().Contain(code);
    }

    [Fact]
    public async Task Feature_code_that_is_not_snake_case_returns_400()
    {
        var officer = await OfficerAsync();

        var response = await officer.PostAsJsonAsync("/api/features", new { code = "smart-tv", name = "Smart TV" });

        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("Code", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Students_cannot_write_reference_data()
    {
        var student = Student();

        (await student.PostAsJsonAsync("/api/buildings", new { code = "XX", name = "X" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.PostAsJsonAsync("/api/features", new { code = "xx", name = "X" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deleting_a_feature_or_building_that_a_room_uses_returns_409_in_use()
    {
        var officer = await OfficerAsync();
        var featureCode = UniqueFeatureCode();
        var feature = await (await officer.PostAsJsonAsync("/api/features", new { code = featureCode, name = "Temp" })).ReadJsonAsync();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(fixture.Factory, FacilitiesTestData.UniquePrefix());
        await FacilitiesTestData.CreateRoomAsync(
            fixture.Factory, buildingId, FacilitiesTestData.UniquePrefix(), RoomTypes.SeminarRoom, 20, [featureCode]);

        var deleteFeature = await officer.DeleteAsync($"/api/features/{feature.GetProperty("id").GetInt64()}");
        var deleteBuilding = await officer.DeleteAsync($"/api/buildings/{buildingId}");
        var renameFeature = await officer.PutAsJsonAsync(
            $"/api/features/{feature.GetProperty("id").GetInt64()}", new { code = UniqueFeatureCode(), name = "Temp" });

        (await deleteFeature.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
        (await deleteBuilding.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
        (await renameFeature.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
    }

    [Fact]
    public async Task Changing_the_code_of_an_unused_feature_returns_400_on_code()
    {
        var officer = await OfficerAsync();
        var code = UniqueFeatureCode();
        var id = (await (await officer.PostAsJsonAsync("/api/features", new { code, name = "Temp" })).ReadJsonAsync()).GetProperty("id").GetInt64();

        var response = await officer.PutAsJsonAsync($"/api/features/{id}", new { code = UniqueFeatureCode(), name = "Temp" });

        var errors = (await response.ShouldBeProblemAsync(400)).GetProperty("errors");
        errors.GetProperty("Code")[0].GetString().Should().Be("Codes can't be changed");
        (await (await officer.GetAsync($"/api/features/{id}")).ReadJsonAsync()).GetProperty("code").GetString().Should().Be(code);
    }

    [Fact]
    public async Task Editing_only_the_name_of_an_in_use_feature_returns_200()
    {
        var officer = await OfficerAsync();
        var code = UniqueFeatureCode();
        var id = (await (await officer.PostAsJsonAsync("/api/features", new { code, name = "Temp" })).ReadJsonAsync()).GetProperty("id").GetInt64();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(fixture.Factory, FacilitiesTestData.UniquePrefix());
        await FacilitiesTestData.CreateRoomAsync(
            fixture.Factory, buildingId, FacilitiesTestData.UniquePrefix(), RoomTypes.SeminarRoom, 20, [code]);

        // The same code in other casing and with spaces normalizes to the stored one, so it is not a change.
        var response = await officer.PutAsJsonAsync($"/api/features/{id}", new { code = $" {code.ToUpperInvariant()} ", name = "Renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("name").GetString().Should().Be("Renamed");
    }

    [Fact]
    public async Task Duplicate_feature_code_on_create_returns_409()
    {
        var officer = await OfficerAsync();
        var code = UniqueFeatureCode();
        (await officer.PostAsJsonAsync("/api/features", new { code, name = "A" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await officer.PostAsJsonAsync("/api/features", new { code, name = "B" });

        (await duplicate.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("Feature code is already taken");
    }

    [Fact]
    public async Task A_feature_covering_an_equipment_type_cannot_be_recoded_or_deleted()
    {
        var officer = await OfficerAsync();
        var code = UniqueFeatureCode();
        var id = (await (await officer.PostAsJsonAsync("/api/features", new { code, name = "Temp" })).ReadJsonAsync()).GetProperty("id").GetInt64();
        await EquipmentTestData.CreateTypeAsync(fixture.Factory, coveredByFeatureCode: code);

        var recode = await officer.PutAsJsonAsync($"/api/features/{id}", new { code = UniqueFeatureCode(), name = "Temp" });
        var delete = await officer.DeleteAsync($"/api/features/{id}");

        (await recode.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
        (await delete.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
    }

    [Fact]
    public async Task A_feature_required_by_a_booking_request_cannot_be_recoded_or_deleted()
    {
        var officer = await OfficerAsync();
        var code = UniqueFeatureCode();
        var id = (await (await officer.PostAsJsonAsync("/api/features", new { code, name = "Temp" })).ReadJsonAsync()).GetProperty("id").GetInt64();
        var (student, _, clubId) = await BookingRequestTestData.StudentRepAsync(fixture.Factory);
        (await student.PostAsJsonAsync(BookingRequestTestData.Url, BookingRequestTestData.Body(clubId, features: [code])))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var recode = await officer.PutAsJsonAsync($"/api/features/{id}", new { code = UniqueFeatureCode(), name = "Temp" });
        var delete = await officer.DeleteAsync($"/api/features/{id}");

        (await recode.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
        (await delete.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
    }

    [Fact]
    public async Task Deleting_an_unused_feature_returns_204()
    {
        var officer = await OfficerAsync();
        var feature = await (await officer.PostAsJsonAsync("/api/features", new { code = UniqueFeatureCode(), name = "Temp" })).ReadJsonAsync();

        var response = await officer.DeleteAsync($"/api/features/{feature.GetProperty("id").GetInt64()}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Inactive_buildings_are_listed_for_officers_only()
    {
        var officer = await OfficerAsync();
        var code = FacilitiesTestData.UniquePrefix();
        var id = (await (await officer.PostAsJsonAsync("/api/buildings", new { code, name = "Old" })).ReadJsonAsync()).GetProperty("id").GetInt64();
        (await officer.PutAsJsonAsync($"/api/buildings/{id}", new { code, name = "Old", isActive = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var studentCodes = (await (await Student().GetAsync("/api/buildings")).ReadJsonAsync()).EnumerateArray()
            .Select(b => b.GetProperty("code").GetString());
        var officerCodes = (await (await officer.GetAsync("/api/buildings")).ReadJsonAsync()).EnumerateArray()
            .Select(b => b.GetProperty("code").GetString());

        studentCodes.Should().NotContain(code);
        officerCodes.Should().Contain(code);
    }
}
