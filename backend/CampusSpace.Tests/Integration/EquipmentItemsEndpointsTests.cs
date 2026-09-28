using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class EquipmentItemsEndpointsTests(PostgresFixture fixture)
{
    private async Task<HttpClient> OfficerAsync() =>
        (await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer)).Client;

    private async Task<HttpClient> TechnicianAsync() =>
        (await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.LabTechnician)).Client;

    private static object Item(
        string assetTag, long typeId, string status = EquipmentItemStatuses.Available,
        string condition = EquipmentConditions.Good, string? notes = null) =>
        new { assetTag, typeId, condition, status, notes };

    private static object Item(EquipmentItem item, string? status = null, string? condition = null, long? typeId = null,
        string? assetTag = null, string? notes = null) =>
        Item(assetTag ?? item.AssetTag, typeId ?? item.TypeId, status ?? item.Status, condition ?? item.Condition, notes ?? item.Notes);

    private static async Task<string> ErrorAsync(HttpResponseMessage response, string field) =>
        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty(field)[0].GetString()!;

    [Fact]
    public async Task Students_and_lecturers_get_403_and_anonymous_gets_401()
    {
        (await TestAuth.CreateClient(fixture.Factory, Roles.Student).GetAsync("/api/equipment-items"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestAuth.CreateClient(fixture.Factory, Roles.Lecturer).GetAsync("/api/equipment-items"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await (await fixture.Factory.CreateClient().GetAsync("/api/equipment-items")).ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Officer_creates_an_item_with_a_normalized_tag_and_a_technician_updates_it()
    {
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var tag = EquipmentTestData.UniqueAssetTag();

        var created = await (await OfficerAsync()).PostAsJsonAsync("/api/equipment-items", Item($" {tag.ToLowerInvariant()} ", type.Id));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.ReadJsonAsync();
        var id = body.GetProperty("id").GetInt64();
        created.Headers.Location!.AbsolutePath.Should().Be($"/api/equipment-items/{id}");
        body.GetProperty("assetTag").GetString().Should().Be(tag);
        body.GetProperty("typeCode").GetString().Should().Be(type.Code);
        body.GetProperty("typeName").GetString().Should().Be(type.Name);

        var technician = await TechnicianAsync();
        var updated = await technician.PutAsJsonAsync($"/api/equipment-items/{id}",
            Item(tag, type.Id, EquipmentItemStatuses.UnderRepair, EquipmentConditions.Damaged, "Cracked grille"));

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await updated.ReadJsonAsync();
        after.GetProperty("status").GetString().Should().Be(EquipmentItemStatuses.UnderRepair);
        after.GetProperty("condition").GetString().Should().Be(EquipmentConditions.Damaged);
        after.GetProperty("notes").GetString().Should().Be("Cracked grille");
        (await technician.GetAsync($"/api/equipment-items/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Duplicate_asset_tag_returns_409_and_unknown_type_returns_400()
    {
        var officer = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var existing = await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id);

        var duplicate = await officer.PostAsJsonAsync("/api/equipment-items", Item(existing.AssetTag.ToLowerInvariant(), type.Id));
        var unknownType = await officer.PostAsJsonAsync("/api/equipment-items", Item(EquipmentTestData.UniqueAssetTag(), long.MaxValue));

        (await duplicate.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("Asset tag is already taken");
        (await ErrorAsync(unknownType, "TypeId")).Should().Be("Equipment type does not exist.");
    }

    [Fact]
    public async Task Status_on_loan_cannot_be_set_through_crud()
    {
        var officer = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var item = await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id);

        var create = await officer.PostAsJsonAsync("/api/equipment-items",
            Item(EquipmentTestData.UniqueAssetTag(), type.Id, EquipmentItemStatuses.OnLoan));
        var update = await officer.PutAsJsonAsync($"/api/equipment-items/{item.Id}", Item(item, status: EquipmentItemStatuses.OnLoan));

        (await ErrorAsync(create, "Status")).Should().Be("Only loans can set OnLoan.");
        (await ErrorAsync(update, "Status")).Should().Be("Only loans can set OnLoan.");
    }

    [Fact]
    public async Task Damaged_items_must_be_under_repair_or_retired()
    {
        var officer = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var item = await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id);

        var create = await officer.PostAsJsonAsync("/api/equipment-items",
            Item(EquipmentTestData.UniqueAssetTag(), type.Id, EquipmentItemStatuses.Available, EquipmentConditions.Damaged));
        var update = await officer.PutAsJsonAsync($"/api/equipment-items/{item.Id}", Item(item, condition: EquipmentConditions.Damaged));
        var retired = await officer.PutAsJsonAsync($"/api/equipment-items/{item.Id}",
            Item(item, status: EquipmentItemStatuses.Retired, condition: EquipmentConditions.Damaged));

        (await ErrorAsync(create, "Condition")).Should().Be("Damaged items must be UnderRepair or Retired.");
        (await ErrorAsync(update, "Condition")).Should().Be("Damaged items must be UnderRepair or Retired.");
        retired.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_items_type_cannot_change()
    {
        var officer = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var other = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var item = await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id);

        var response = await officer.PutAsJsonAsync($"/api/equipment-items/{item.Id}", Item(item, typeId: other.Id));

        (await ErrorAsync(response, "TypeId")).Should().Be("An item's type can't be changed");
    }

    [Fact]
    public async Task An_item_on_loan_accepts_only_a_notes_change()
    {
        var officer = await OfficerAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var other = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        // Inserted directly so this test covers the rule on its own; Only_loans_set_and_clear_OnLoan uses a real checkout.
        var item = await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id, EquipmentItemStatuses.OnLoan);
        var url = $"/api/equipment-items/{item.Id}";

        var status = await officer.PutAsJsonAsync(url, Item(item, status: EquipmentItemStatuses.Available));
        var condition = await officer.PutAsJsonAsync(url, Item(item, condition: EquipmentConditions.MinorWear));
        var typeId = await officer.PutAsJsonAsync(url, Item(item, typeId: other.Id));
        var assetTag = await officer.PutAsJsonAsync(url, Item(item, assetTag: EquipmentTestData.UniqueAssetTag()));
        var notes = await officer.PutAsJsonAsync(url, Item(item, notes: "Borrowed by Robotics Club"));

        (await ErrorAsync(status, "Status")).Should().Be("Item is on loan; use check-in");
        (await ErrorAsync(condition, "Condition")).Should().Be("Item is on loan; use check-in");
        (await ErrorAsync(typeId, "TypeId")).Should().Be("Item is on loan; use check-in");
        (await ErrorAsync(assetTag, "AssetTag")).Should().Be("Item is on loan; use check-in");
        notes.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await notes.ReadJsonAsync();
        body.GetProperty("notes").GetString().Should().Be("Borrowed by Robotics Club");
        body.GetProperty("status").GetString().Should().Be(EquipmentItemStatuses.OnLoan);
    }

    [Fact]
    public async Task Only_loans_set_and_clear_OnLoan()
    {
        var officer = await OfficerAsync();
        var start = DateTimeOffset.UtcNow.AddMinutes(10);
        var handover = await LoanTestData.HandoverAsync(fixture.Factory, start, start.AddHours(2));
        var (tech, _) = await LoanTestData.TechnicianAsync(fixture.Factory);
        var loanId = await LoanTestData.CheckoutOkAsync(tech, handover.BookingId, handover.ItemIds[0]);
        var item = await LoanTestData.ItemAsync(fixture.Factory, handover.ItemIds[0]);
        item.Status.Should().Be(EquipmentItemStatuses.OnLoan);
        var url = $"/api/equipment-items/{item.Id}";

        // The item endpoints can't clear OnLoan, whoever calls them.
        var available = await officer.PutAsJsonAsync(url, Item(item, status: EquipmentItemStatuses.Available));
        var repair = await tech.PutAsJsonAsync(url, Item(item, status: EquipmentItemStatuses.UnderRepair));
        (await ErrorAsync(available, "Status")).Should().Be("Item is on loan; use check-in");
        (await ErrorAsync(repair, "Status")).Should().Be("Item is on loan; use check-in");
        (await officer.PutAsJsonAsync(url, Item(item, notes: "Out with the drama club"))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Check-in clears it; after that the item is edited normally again, but OnLoan still can't be set.
        (await LoanTestData.CheckInAsync(tech, loanId, EquipmentConditions.MinorWear)).StatusCode.Should().Be(HttpStatusCode.OK);
        item = await LoanTestData.ItemAsync(fixture.Factory, item.Id);
        item.Status.Should().Be(EquipmentItemStatuses.Available);
        (await ErrorAsync(await officer.PutAsJsonAsync(url, Item(item, status: EquipmentItemStatuses.OnLoan)), "Status"))
            .Should().Be("Only loans can set OnLoan.");
        (await officer.PutAsJsonAsync(url, Item(item, status: EquipmentItemStatuses.Retired))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_filters_by_type_status_and_condition_and_sorts()
    {
        var technician = await TechnicianAsync();
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var prefix = "EQ-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id, assetTag: $"{prefix}-001");
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id, EquipmentItemStatuses.UnderRepair, EquipmentConditions.Damaged, $"{prefix}-002");
        await EquipmentTestData.CreateItemAsync(fixture.Factory, type.Id, EquipmentItemStatuses.Available, EquipmentConditions.MinorWear, $"{prefix}-003");

        async Task<string[]> TagsAsync(string query)
        {
            var response = await technician.GetAsync($"/api/equipment-items?typeId={type.Id}&{query}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("assetTag").GetString()!).ToArray();
        }

        (await TagsAsync("sort=-assetTag")).Should().Equal($"{prefix}-003", $"{prefix}-002", $"{prefix}-001");
        (await TagsAsync("status=Available")).Should().Equal($"{prefix}-001", $"{prefix}-003");
        (await TagsAsync("condition=Damaged")).Should().Equal($"{prefix}-002");
        (await TagsAsync($"search={prefix.ToLowerInvariant()}-00&sort=condition")).Should().Equal($"{prefix}-002", $"{prefix}-001", $"{prefix}-003");
        (await TagsAsync("search=nothing-matches")).Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_sort_or_status_filter_returns_400()
    {
        var technician = await TechnicianAsync();

        (await technician.GetAsync("/api/equipment-items?sort=price")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await technician.GetAsync("/api/equipment-items?status=Lost")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
