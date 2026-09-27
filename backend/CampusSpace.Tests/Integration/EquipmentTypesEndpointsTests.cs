using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class EquipmentTypesEndpointsTests(PostgresFixture fixture)
{
    private async Task<(HttpClient Client, long UserId)> OfficerAsync() =>
        await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);

    private HttpClient Student() => TestAuth.CreateClient(fixture.Factory, Roles.Student);

    private static object NewType(string code, decimal fee = 500m, string? coveredByFeatureCode = null, string category = EquipmentCategories.Audio) =>
        new { code, name = "Wireless microphone", category, feePerBooking = fee, coveredByFeatureCode };

    private static long IdOf(System.Text.Json.JsonElement body) => body.GetProperty("id").GetInt64();

    private static string[] CodesOf(System.Text.Json.JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("code").GetString()!).ToArray();

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/api/equipment-types");

        await response.ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Student_can_read_types_and_categories_but_cannot_write()
    {
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var student = Student();

        (await student.GetAsync("/api/equipment-types")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.GetAsync($"/api/equipment-types/{type.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.GetAsync($"/api/equipment-types/{type.Id}/substitutes")).StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = await student.GetAsync("/api/equipment-types/categories");
        (await categories.ReadJsonAsync()).EnumerateArray().Select(c => c.GetString())
            .Should().Equal("Audio", "Visual", "Computing", "Presentation", "Accessory");

        (await student.PostAsJsonAsync("/api/equipment-types", NewType(EquipmentTestData.UniqueTypeCode())))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.PutAsJsonAsync($"/api/equipment-types/{type.Id}/substitutes", new { substituteTypeIds = Array.Empty<long>() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.DeleteAsync($"/api/equipment-types/{type.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deleting_a_type_used_by_a_booking_request_line_returns_409_In_use()
    {
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var (student, _, clubId) = await BookingRequestTestData.StudentRepAsync(fixture.Factory);
        (await student.PostAsJsonAsync(BookingRequestTestData.Url,
                BookingRequestTestData.Body(clubId, equipment: [BookingRequestTestData.Line(type.Id, 2)])))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var (officer, _) = await OfficerAsync();

        var response = await officer.DeleteAsync($"/api/equipment-types/{type.Id}");

        // RequestedEquipmentLines.TypeId is RESTRICT, so PostgreSQL rejects the delete (23503).
        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().EquipmentTypes.AnyAsync(t => t.Id == type.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Officer_creates_updates_and_deletes_a_type()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(fixture.Factory);
        var (officer, _) = await OfficerAsync();
        var code = EquipmentTestData.UniqueTypeCode();

        var created = await officer.PostAsJsonAsync("/api/equipment-types", NewType($" {code.ToLowerInvariant()} ", 1500m, " Projector "));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.ReadJsonAsync();
        var id = IdOf(body);
        created.Headers.Location!.AbsolutePath.Should().Be($"/api/equipment-types/{id}");
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("feePerBooking").GetDecimal().Should().Be(1500m);
        body.GetProperty("coveredByFeatureCode").GetString().Should().Be("projector");
        body.GetProperty("coveredByFeatureName").GetString().Should().Be("projector");
        body.GetProperty("itemCounts").GetProperty("total").GetInt32().Should().Be(0);
        body.GetProperty("substitutes").GetArrayLength().Should().Be(0);

        var updated = await officer.PutAsJsonAsync($"/api/equipment-types/{id}",
            new { code, name = "Renamed", category = EquipmentCategories.Visual, feePerBooking = 99.5m, coveredByFeatureCode = "" });

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await updated.ReadJsonAsync();
        after.GetProperty("name").GetString().Should().Be("Renamed");
        after.GetProperty("category").GetString().Should().Be(EquipmentCategories.Visual);
        after.GetProperty("feePerBooking").GetDecimal().Should().Be(99.5m);
        after.GetProperty("coveredByFeatureCode").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        (await officer.DeleteAsync($"/api/equipment-types/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await officer.GetAsync($"/api/equipment-types/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicate_code_returns_409_and_a_changed_code_returns_400()
    {
        var (officer, _) = await OfficerAsync();
        var code = EquipmentTestData.UniqueTypeCode();
        var id = IdOf(await (await officer.PostAsJsonAsync("/api/equipment-types", NewType(code))).ReadJsonAsync());

        var duplicate = await officer.PostAsJsonAsync("/api/equipment-types", NewType(code.ToLowerInvariant()));
        var recode = await officer.PutAsJsonAsync($"/api/equipment-types/{id}", NewType(EquipmentTestData.UniqueTypeCode()));

        (await duplicate.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("Equipment type code is already taken");
        (await recode.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty("Code")[0].GetString()
            .Should().Be("Codes can't be changed");
    }

    [Theory]
    [InlineData("MIC_WIRELESS", "Code")]
    [InlineData("A", "Code")]
    [InlineData("-MIC", "Code")]
    public async Task Bad_code_returns_400_on_code(string code, string field)
    {
        var (officer, _) = await OfficerAsync();

        var response = await officer.PostAsJsonAsync("/api/equipment-types", NewType(code));

        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_category_negative_fee_or_three_decimals_return_400_on_the_field()
    {
        var (officer, _) = await OfficerAsync();

        var category = await officer.PostAsJsonAsync("/api/equipment-types", NewType(EquipmentTestData.UniqueTypeCode(), category: "Furniture"));
        var negative = await officer.PostAsJsonAsync("/api/equipment-types", NewType(EquipmentTestData.UniqueTypeCode(), fee: -1m));
        var decimals = await officer.PostAsJsonAsync("/api/equipment-types", NewType(EquipmentTestData.UniqueTypeCode(), fee: 1.005m));

        (await category.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("Category", out _).Should().BeTrue();
        (await negative.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("FeePerBooking", out _).Should().BeTrue();
        (await decimals.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("FeePerBooking", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_covered_by_feature_code_returns_400_on_that_field()
    {
        var (officer, _) = await OfficerAsync();

        var response = await officer.PostAsJsonAsync("/api/equipment-types", NewType(EquipmentTestData.UniqueTypeCode(), coveredByFeatureCode: "no_such_feature"));

        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty("CoveredByFeatureCode")[0].GetString()
            .Should().Be("Unknown feature code: no_such_feature.");
    }

    [Fact]
    public async Task Deleting_a_type_that_has_items_returns_409_in_use()
    {
        var (officer, _) = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id);

        var response = await officer.DeleteAsync($"/api/equipment-types/{type.Id}");

        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
    }

    [Fact]
    public async Task Detail_counts_items_per_status_and_names_the_covering_feature()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(fixture.Factory);
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory, coveredByFeatureCode: "sound_system");
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id);
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id);
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id, EquipmentItemStatuses.OnLoan);
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id, EquipmentItemStatuses.UnderRepair, EquipmentConditions.Damaged);
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id, EquipmentItemStatuses.Retired);

        var body = await (await Student().GetAsync($"/api/equipment-types/{type.Id}")).ReadJsonAsync();

        body.GetProperty("coveredByFeatureName").GetString().Should().Be("sound_system");
        var counts = body.GetProperty("itemCounts");
        counts.GetProperty("total").GetInt32().Should().Be(5);
        counts.GetProperty("available").GetInt32().Should().Be(2);
        counts.GetProperty("onLoan").GetInt32().Should().Be(1);
        counts.GetProperty("underRepair").GetInt32().Should().Be(1);
        counts.GetProperty("retired").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task List_searches_filters_sorts_and_pages()
    {
        var marker = "L" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await EquipmentTestData.CreateTypeAsync(fixture.Factory, $"{marker}-A", EquipmentCategories.Audio, 300m);
        await EquipmentTestData.CreateTypeAsync(fixture.Factory, $"{marker}-B", EquipmentCategories.Audio, 100m);
        await EquipmentTestData.CreateTypeAsync(fixture.Factory, $"{marker}-C", EquipmentCategories.Visual, 200m);
        var student = Student();

        var byFee = await (await student.GetAsync($"/api/equipment-types?search={marker.ToLowerInvariant()}&sort=-fee")).ReadJsonAsync();
        var audio = await (await student.GetAsync($"/api/equipment-types?search={marker}&category=Audio")).ReadJsonAsync();
        var page2 = await (await student.GetAsync($"/api/equipment-types?search={marker}&sort=code&page=2&pageSize=2")).ReadJsonAsync();

        CodesOf(byFee).Should().Equal($"{marker}-A", $"{marker}-C", $"{marker}-B");
        CodesOf(audio).Should().Equal($"{marker}-A", $"{marker}-B");
        CodesOf(page2).Should().Equal($"{marker}-C");
        page2.GetProperty("total").GetInt32().Should().Be(3);
        page2.GetProperty("page").GetInt32().Should().Be(2);
        page2.GetProperty("pageSize").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Invalid_sort_or_category_filter_returns_400()
    {
        (await Student().GetAsync("/api/equipment-types?sort=price")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Student().GetAsync("/api/equipment-types?category=Furniture")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Replacing_substitutes_sets_the_whole_list()
    {
        var (officer, _) = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory, "S" + EquipmentTestData.UniqueTypeCode());
        var a = await EquipmentTestData.CreateTypeAsync(fixture.Factory, "A" + EquipmentTestData.UniqueTypeCode());
        var b = await EquipmentTestData.CreateTypeAsync(fixture.Factory, "B" + EquipmentTestData.UniqueTypeCode());
        var c = await EquipmentTestData.CreateTypeAsync(fixture.Factory, "C" + EquipmentTestData.UniqueTypeCode());

        var first = await officer.PutAsJsonAsync($"/api/equipment-types/{type.Id}/substitutes", new { substituteTypeIds = new[] { b.Id, a.Id } });
        var second = await officer.PutAsJsonAsync($"/api/equipment-types/{type.Id}/substitutes", new { substituteTypeIds = new[] { c.Id, b.Id } });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.ReadJsonAsync()).EnumerateArray().Select(s => s.GetProperty("id").GetInt64()).Should().Equal(a.Id, b.Id);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await (await Student().GetAsync($"/api/equipment-types/{type.Id}")).ReadJsonAsync();
        detail.GetProperty("substitutes").EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().Equal(b.Code, c.Code);
        var list = await (await Student().GetAsync($"/api/equipment-types/{type.Id}/substitutes")).ReadJsonAsync();
        list.EnumerateArray().Select(s => s.GetProperty("id").GetInt64()).Should().Equal(b.Id, c.Id);

        var cleared = await officer.PutAsJsonAsync($"/api/equipment-types/{type.Id}/substitutes", new { substituteTypeIds = Array.Empty<long>() });
        (await cleared.ReadJsonAsync()).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Self_unknown_or_repeated_substitutes_return_400()
    {
        var (officer, _) = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var other = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var url = $"/api/equipment-types/{type.Id}/substitutes";

        var self = await officer.PutAsJsonAsync(url, new { substituteTypeIds = new[] { type.Id } });
        var unknown = await officer.PutAsJsonAsync(url, new { substituteTypeIds = new[] { other.Id, long.MaxValue } });
        var repeated = await officer.PutAsJsonAsync(url, new { substituteTypeIds = new[] { other.Id, other.Id } });

        (await self.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty("SubstituteTypeIds")[0].GetString()
            .Should().Be("A type cannot be its own substitute.");
        (await unknown.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty("SubstituteTypeIds")[0].GetString()
            .Should().Be($"Unknown equipment type ids: {long.MaxValue}.");
        (await repeated.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("SubstituteTypeIds", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_a_type_removes_substitute_pairs_on_both_sides()
    {
        var (officer, _) = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var other = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        (await officer.PutAsJsonAsync($"/api/equipment-types/{type.Id}/substitutes", new { substituteTypeIds = new[] { other.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await officer.PutAsJsonAsync($"/api/equipment-types/{other.Id}/substitutes", new { substituteTypeIds = new[] { type.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await officer.DeleteAsync($"/api/equipment-types/{type.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.EquipmentSubstitutes.CountAsync(s => s.TypeId == type.Id || s.SubstituteTypeId == type.Id)).Should().Be(0);
        (await db.EquipmentTypes.AnyAsync(t => t.Id == other.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Creating_a_type_writes_one_audit_row_with_property_names_only()
    {
        var (officer, userId) = await OfficerAsync();
        var code = EquipmentTestData.UniqueTypeCode();

        var id = IdOf(await (await officer.PostAsJsonAsync("/api/equipment-types", NewType(code, 1234.56m))).ReadJsonAsync());

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityType == nameof(EquipmentType) && a.EntityId == id.ToString());
        log.Action.Should().Be(AuditActions.Created);
        log.UserId.Should().Be(userId);
        log.DetailsJson.Should().Contain("\"Code\"").And.Contain("\"FeePerBooking\"").And.Contain("\"Category\"");
        log.DetailsJson.Should().NotContain(code).And.NotContain("1234.56").And.NotContain("Wireless microphone");
    }
}
