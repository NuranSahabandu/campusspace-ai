using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class BookingRequestsReadTests(PostgresFixture fixture)
{
    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static async Task<long> SubmitAsync(HttpClient client, Dictionary<string, object?> body)
    {
        var response = await client.PostAsJsonAsync(Url, body);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"{Url}?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ReadJsonAsync();
    }

    private static List<long> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();

    [Fact]
    public async Task Requesters_see_only_their_own_rows_and_an_officer_sees_all()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (alice, _, aliceClub) = await StudentRepAsync(Factory);
        var (bob, _, bobClub) = await StudentRepAsync(Factory);
        var aliceId = await SubmitAsync(alice, Body(aliceClub, purpose: $"Alice {tag}"));
        var bobId = await SubmitAsync(bob, Body(bobClub, purpose: $"Bob {tag}"));

        var alicePage = await ListAsync(alice, "pageSize=100");
        Ids(alicePage).Should().Equal(aliceId);
        alicePage.GetProperty("total").GetInt32().Should().Be(1);

        var officer = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);
        Ids(await ListAsync(officer, $"search={tag}")).Should().BeEquivalentTo([aliceId, bobId]);
    }

    [Fact]
    public async Task A_row_has_the_summary_fields()
    {
        var (client, userId, clubId) = await StudentRepAsync(Factory);
        var start = FutureStart(weekdaysAhead: 40, hour: 9);
        var id = await SubmitAsync(client, Body(clubId, purpose: "Summary check", attendees: 12, start: start, hours: 2, budget: 1500.5m));

        var row = (await ListAsync(client, "")).GetProperty("items")[0];

        row.GetProperty("id").GetInt64().Should().Be(id);
        row.GetProperty("purpose").GetString().Should().Be("Summary check");
        row.GetProperty("status").GetString().Should().Be(RequestStatuses.AgentProcessing);
        row.GetProperty("requestedStart").GetDateTime().Should().Be(start.UtcDateTime);
        row.GetProperty("requestedEnd").GetDateTime().Should().Be(start.AddHours(2).UtcDateTime);
        row.GetProperty("attendees").GetInt32().Should().Be(12);
        row.GetProperty("budgetLkr").GetDecimal().Should().Be(1500.5m);
        row.GetProperty("clubName").GetString().Should().StartWith("Club ");
        row.GetProperty("requesterName").GetString().Should().StartWith("Test Student");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        row.GetProperty("requesterEmail").GetString().Should().Be(me.GetProperty("email").GetString());
        row.GetProperty("createdAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        row.GetProperty("cancelledAt").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("isLateCancellation").GetBoolean().Should().BeFalse();
        row.GetProperty("cancelledByOfficer").GetBoolean().Should().BeFalse();
        userId.Should().BePositive();
    }

    [Fact]
    public async Task Filters_sort_and_paging_work_on_the_callers_rows()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var early = await SubmitAsync(client, Body(clubId, purpose: "Early lab", attendees: 10, start: FutureStart(20)));
        var late = await SubmitAsync(client, Body(clubId, purpose: "Late seminar", attendees: 30, start: FutureStart(22)));
        var middle = await SubmitAsync(client, Body(clubId, purpose: "Middle lab", attendees: 20, start: FutureStart(21)));
        // Submit leaves every request AgentProcessing.
        await MoveAsync(Factory, late, RequestStatuses.PendingApproval);

        Ids(await ListAsync(client, "")).Should().Equal(middle, late, early);
        Ids(await ListAsync(client, "sort=requestedStart")).Should().Equal(early, middle, late);
        Ids(await ListAsync(client, "sort=-attendees")).Should().Equal(late, middle, early);
        Ids(await ListAsync(client, "sort=createdAt")).Should().Equal(early, late, middle);

        Ids(await ListAsync(client, "status=PendingApproval")).Should().Equal(late);
        Ids(await ListAsync(client, "status=AgentProcessing,PendingApproval&sort=requestedStart")).Should().Equal(early, middle, late);
        Ids(await ListAsync(client, "status=AgentProcessing&status=Approved&sort=requestedStart")).Should().Equal(early, middle);
        Ids(await ListAsync(client, "search=LAB&sort=requestedStart")).Should().Equal(early, middle);
        Ids(await ListAsync(client, $"clubId={clubId}&sort=requestedStart")).Should().Equal(early, middle, late);

        var day21 = FutureStart(21).ToString("yyyy-MM-dd");
        var day22 = FutureStart(22).ToString("yyyy-MM-dd");
        Ids(await ListAsync(client, $"from={day21}&to={day21}")).Should().Equal(middle);
        Ids(await ListAsync(client, $"from={day21}&sort=requestedStart")).Should().Equal(middle, late);
        Ids(await ListAsync(client, $"to={day22}&sort=requestedStart")).Should().Equal(early, middle, late);

        var page2 = await ListAsync(client, "sort=requestedStart&page=2&pageSize=2");
        Ids(page2).Should().Equal(late);
        page2.GetProperty("total").GetInt32().Should().Be(3);
        page2.GetProperty("page").GetInt32().Should().Be(2);
        page2.GetProperty("pageSize").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Invalid_sort_status_or_date_range_is_400()
    {
        var (client, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);

        foreach (var (query, field) in new[]
                 {
                     ("sort=purpose", "Sort"), ("status=Draft", "Status"), ("status=Submitted,Nope", "Status"),
                     ("from=2030-02-01&to=2030-01-01", "From"),
                 })
            (await (await client.GetAsync($"{Url}?{query}")).ShouldBeProblemAsync(400))
                .GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(query);
    }

    [Fact]
    public async Task Officer_searches_by_requester_name_email_and_club_but_a_requester_only_by_purpose()
    {
        var (client, userId, clubId) = await StudentRepAsync(Factory);
        var id = await SubmitAsync(client, Body(clubId, purpose: "Quarterly meetup"));
        var officer = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);
        var detail = await (await officer.GetAsync($"{Url}/{id}")).ReadJsonAsync();
        var name = detail.GetProperty("requester").GetProperty("name").GetString()!;
        var email = detail.GetProperty("requester").GetProperty("email").GetString()!;
        var club = detail.GetProperty("club").GetProperty("name").GetString()!;

        Ids(await ListAsync(officer, $"search={Uri.EscapeDataString(name.ToUpperInvariant())}")).Should().Equal(id);
        Ids(await ListAsync(officer, $"search={Uri.EscapeDataString(email)}")).Should().Equal(id);
        Ids(await ListAsync(officer, $"search={Uri.EscapeDataString(club)}")).Should().Equal(id);
        Ids(await ListAsync(client, $"search={Uri.EscapeDataString(name)}")).Should().BeEmpty();
        userId.Should().BePositive();
    }

    [Fact]
    public async Task Detail_and_history_are_for_the_owner_and_officers_only()
    {
        var (owner, _, clubId) = await StudentRepAsync(Factory);
        var id = await SubmitAsync(owner, Body(clubId));
        var (otherStudent, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        var (lecturer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Lecturer);
        var officer = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);

        foreach (var path in new[] { $"{Url}/{id}", $"{Url}/{id}/history" })
        {
            (await owner.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
            (await officer.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
            (await (await otherStudent.GetAsync(path)).ShouldBeProblemAsync(403)).GetProperty("title").GetString()
                .Should().Be("You can only view your own requests");
            await (await lecturer.GetAsync(path)).ShouldBeProblemAsync(403);
            await (await Factory.CreateClient().GetAsync(path)).ShouldBeProblemAsync(401);
        }

        await (await owner.GetAsync($"{Url}/999999999")).ShouldBeProblemAsync(404);
        await (await officer.GetAsync($"{Url}/999999999/history")).ShouldBeProblemAsync(404);
    }

    [Fact]
    public async Task Admin_and_lab_technician_get_403_on_every_route()
    {
        foreach (var role in new[] { Roles.Admin, Roles.LabTechnician })
        {
            var client = TestAuth.CreateClient(Factory, role);
            foreach (var path in new[] { Url, $"{Url}/1", $"{Url}/1/history", $"{Url}/eligibility" })
                (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} {path}");
        }
        (await TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer).GetAsync($"{Url}/eligibility"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Detail_and_history_have_their_shapes_with_history_oldest_first()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(Factory);
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        var (client, userId, clubId) = await StudentRepAsync(Factory);
        var id = await SubmitAsync(client, Body(clubId, features: ["sound_system"], equipment: [Line(type.Id, 3)], notes: "n"));
        await MoveAsync(Factory, id, RequestStatuses.PendingApproval, RequestStatuses.Cancelled);

        var detail = await (await client.GetAsync($"{Url}/{id}")).ReadJsonAsync();

        detail.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "id", "purpose", "status", "attendees", "requestedStart", "requestedEnd", "budgetLkr", "notes", "requester",
            "club", "requiredFeatures", "equipment", "history", "latestProposal",
            "cancelledAt", "isLateCancellation", "cancelledByOfficer", "createdAt", "updatedAt");
        detail.GetProperty("status").GetString().Should().Be(RequestStatuses.Cancelled);
        // Moved to Cancelled directly, not through the cancel operation: the cancellation fields keep their defaults.
        detail.GetProperty("cancelledAt").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("isLateCancellation").GetBoolean().Should().BeFalse();
        detail.GetProperty("cancelledByOfficer").GetBoolean().Should().BeFalse();
        detail.GetProperty("requester").EnumerateObject().Select(p => p.Name).Should().Equal("id", "name", "email");
        detail.GetProperty("requester").GetProperty("id").GetInt64().Should().Be(userId);
        detail.GetProperty("club").EnumerateObject().Select(p => p.Name).Should().Equal("id", "name");
        detail.GetProperty("requiredFeatures")[0].GetProperty("code").GetString().Should().Be("sound_system");
        detail.GetProperty("requiredFeatures")[0].GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        detail.GetProperty("equipment")[0].EnumerateObject().Select(p => p.Name)
            .Should().Equal("typeId", "typeCode", "typeName", "quantity");

        var history = await (await client.GetAsync($"{Url}/{id}/history")).ReadJsonAsync();
        history.GetRawText().Should().Be(detail.GetProperty("history").GetRawText());
        // Submit writes the first two rows (null → Submitted → AgentProcessing); MoveAsync the other two.
        history.GetArrayLength().Should().Be(4);
        history[0].EnumerateObject().Select(p => p.Name)
            .Should().Equal("fromStatus", "toStatus", "changedById", "changedByName", "reason", "changedAt");
        history[0].GetProperty("toStatus").GetString().Should().Be(RequestStatuses.Submitted);
        history[0].GetProperty("changedById").GetInt64().Should().Be(userId);
        history[0].GetProperty("changedByName").GetString().Should().StartWith("Test Student");
        history[1].GetProperty("fromStatus").GetString().Should().Be(RequestStatuses.Submitted);
        history[1].GetProperty("toStatus").GetString().Should().Be(RequestStatuses.AgentProcessing);
        history[1].GetProperty("changedById").GetInt64().Should().Be(userId);
        history[3].GetProperty("fromStatus").GetString().Should().Be(RequestStatuses.PendingApproval);
        history[3].GetProperty("toStatus").GetString().Should().Be(RequestStatuses.Cancelled);
        history[3].GetProperty("changedById").ValueKind.Should().Be(JsonValueKind.Null);
        history[3].GetProperty("changedByName").ValueKind.Should().Be(JsonValueKind.Null);
        history[3].GetProperty("reason").GetString().Should().Be("test");
    }
}
