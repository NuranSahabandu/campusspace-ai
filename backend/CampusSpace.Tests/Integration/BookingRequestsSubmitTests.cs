using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class BookingRequestsSubmitTests(PostgresFixture fixture)
{
    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response) =>
        (await response.ShouldBeProblemAsync(400)).GetProperty("errors");

    private static async Task<string> ErrorAsync(HttpResponseMessage response, string field) =>
        (await ErrorsAsync(response)).GetProperty(field)[0].GetString()!;

    [Fact]
    public async Task Student_representative_submits_and_gets_201_with_the_request_its_lines_and_first_history_row()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(Factory);
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        var (client, userId, clubId) = await StudentRepAsync(Factory);
        var start = FutureStart(daysAhead: 25, hour: 14);

        var response = await client.PostAsJsonAsync(Url, Body(clubId, purpose: "  Robotics workshop  ", start: start,
            features: ["Computers", " projector ", "computers"], equipment: [Line(type.Id, 2)], notes: "prefer near the main building"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.ReadJsonAsync();
        var id = body.GetProperty("id").GetInt64();
        response.Headers.Location!.AbsolutePath.Should().Be($"{Url}/{id}");
        body.GetProperty("status").GetString().Should().Be(RequestStatuses.Submitted);
        body.GetProperty("purpose").GetString().Should().Be("Robotics workshop");
        body.GetProperty("requestedStart").GetDateTime().Should().Be(start.UtcDateTime);
        body.GetProperty("requestedStart").GetString().Should().EndWith("Z");
        body.GetProperty("budgetLkr").GetDecimal().Should().Be(8000m);
        body.GetProperty("notes").GetString().Should().Be("prefer near the main building");
        body.GetProperty("requester").GetProperty("id").GetInt64().Should().Be(userId);
        body.GetProperty("club").GetProperty("id").GetInt64().Should().Be(clubId);
        body.GetProperty("requiredFeatures").EnumerateArray().Select(f => f.GetProperty("code").GetString())
            .Should().Equal("computers", "projector");
        body.GetProperty("equipment")[0].GetProperty("typeCode").GetString().Should().Be(type.Code);
        body.GetProperty("equipment")[0].GetProperty("quantity").GetInt32().Should().Be(2);
        body.GetProperty("latestProposal").ValueKind.Should().Be(JsonValueKind.Null);
        var history = body.GetProperty("history");
        history.GetArrayLength().Should().Be(1);
        history[0].GetProperty("fromStatus").ValueKind.Should().Be(JsonValueKind.Null);
        history[0].GetProperty("toStatus").GetString().Should().Be(RequestStatuses.Submitted);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.BookingRequests.AsNoTracking().Include(r => r.EquipmentLines).Include(r => r.StatusHistory)
            .SingleAsync(r => r.Id == id);
        saved.RequesterId.Should().Be(userId);
        saved.RequestedStart.Should().Be(start.UtcDateTime);
        saved.EquipmentLines.Should().ContainSingle(l => l.TypeId == type.Id && l.Quantity == 2);
        saved.StatusHistory.Should().ContainSingle(h => h.FromStatus == null && h.ToStatus == RequestStatuses.Submitted && h.ChangedById == userId);
    }

    [Fact]
    public async Task Student_without_a_valid_club_gets_400_on_ClubId()
    {
        var (client, userId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        var (_, otherId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        var someoneElses = await CreateClubAsync(Factory, representativeId: otherId, memberId: userId);
        var inactive = await CreateClubAsync(Factory, representativeId: userId, isActive: false);

        foreach (var clubId in new long?[] { null, someoneElses, inactive, 999_999_999 })
            (await ErrorAsync(await client.PostAsJsonAsync(Url, Body(clubId)), "ClubId"))
                .Should().Be(BookingRequestService.NotRepresentativeMessage, $"club {clubId}");
    }

    [Fact]
    public async Task Lecturer_submits_without_a_club_but_gets_400_with_one()
    {
        var (lecturer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Lecturer);
        var clubId = await CreateClubAsync(Factory);

        (await ErrorAsync(await lecturer.PostAsJsonAsync(Url, Body(clubId)), "ClubId"))
            .Should().Be(BookingRequestService.LecturerClubMessage);

        var created = await lecturer.PostAsJsonAsync(Url, Body(clubId: null, purpose: "Guest lecture", budget: 0m));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await created.ReadJsonAsync()).GetProperty("club").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Other_roles_get_403_and_anonymous_gets_401()
    {
        foreach (var role in new[] { Roles.LabTechnician, Roles.FacilitiesOfficer, Roles.Admin })
            (await TestAuth.CreateClient(Factory, role).PostAsJsonAsync(Url, Body()))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
        await (await Factory.CreateClient().PostAsJsonAsync(Url, Body())).ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Bad_times_are_400()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);

        var zeroLength = Body(clubId, hours: 0);
        (await ErrorsAsync(await client.PostAsJsonAsync(Url, zeroLength))).TryGetProperty("RequestedEnd", out _).Should().BeTrue();

        var past = Body(clubId, start: DateTimeOffset.UtcNow.AddHours(-1));
        (await ErrorAsync(await client.PostAsJsonAsync(Url, past), "RequestedStart")).Should().Be("Start must be in the future.");
    }

    [Fact]
    public async Task Several_field_errors_are_reported_together_and_unknown_features_are_listed()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(Factory);
        var (client, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);

        var response = await client.PostAsJsonAsync(Url, Body(clubId: null, start: DateTimeOffset.UtcNow.AddDays(-1),
            budget: 10.005m, features: ["projector", "hologram", "Jetpack"]));

        var errors = await ErrorsAsync(response);
        errors.GetProperty("RequiredFeatures")[0].GetString().Should().Be("Unknown feature codes: hologram, jetpack.");
        errors.GetProperty("BudgetLkr")[0].GetString().Should().Be("Budget can have at most 2 decimal places.");
        errors.GetProperty("RequestedStart")[0].GetString().Should().Be("Start must be in the future.");
        errors.GetProperty("ClubId")[0].GetString().Should().Be(BookingRequestService.NotRepresentativeMessage);
    }

    [Fact]
    public async Task Bad_equipment_lines_and_other_field_limits_are_400()
    {
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        var (client, _, clubId) = await StudentRepAsync(Factory);

        (await ErrorAsync(await client.PostAsJsonAsync(Url, Body(clubId, equipment: [Line(type.Id, 1), Line(type.Id, 2)])), "Equipment"))
            .Should().Contain("only once");
        (await ErrorAsync(await client.PostAsJsonAsync(Url, Body(clubId, equipment: [Line(999_999_999, 1)])), "Equipment"))
            .Should().Be("Unknown equipment types: 999999999.");
        (await client.PostAsJsonAsync(Url, Body(clubId, equipment: [Line(type.Id, 0)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(Url, Body(clubId, equipment: [Line(type.Id, 51)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await ErrorsAsync(await client.PostAsJsonAsync(Url, Body(clubId, attendees: 0)))).TryGetProperty("Attendees", out _).Should().BeTrue();
        (await ErrorsAsync(await client.PostAsJsonAsync(Url, Body(clubId, attendees: 2001)))).TryGetProperty("Attendees", out _).Should().BeTrue();
        (await ErrorsAsync(await client.PostAsJsonAsync(Url, Body(clubId, budget: -1m)))).TryGetProperty("BudgetLkr", out _).Should().BeTrue();
        (await ErrorsAsync(await client.PostAsJsonAsync(Url, Body(clubId, budget: 100_000_000m)))).TryGetProperty("BudgetLkr", out _).Should().BeTrue();
        (await ErrorsAsync(await client.PostAsJsonAsync(Url, Body(clubId, purpose: "   ")))).TryGetProperty("Purpose", out _).Should().BeTrue();
        (await ErrorsAsync(await client.PostAsJsonAsync(Url, Body(clubId, purpose: new string('p', 201))))).TryGetProperty("Purpose", out _).Should().BeTrue();
        (await ErrorsAsync(await client.PostAsJsonAsync(Url, Body(clubId, notes: new string('n', 1001))))).TryGetProperty("Notes", out _).Should().BeTrue();

        (await client.PostAsJsonAsync(Url, Body(clubId, budget: 99_999_999.99m, equipment: [Line(type.Id, 50)])))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_open_request_cap_is_409_and_a_closed_request_frees_a_slot()
    {
        // The shared database keeps the default max_open_requests (3).
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var ids = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var created = await client.PostAsJsonAsync(Url, Body(clubId, purpose: $"Meeting {i}"));
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            ids.Add((await created.ReadJsonAsync()).GetProperty("id").GetInt64());
        }

        var blocked = await client.PostAsJsonAsync(Url, Body(clubId));
        (await blocked.ShouldBeProblemAsync(409)).GetProperty("title").GetString()
            .Should().Be("You already have 3 open requests (the limit is 3)");

        // PendingApproval is still open; Rejected is closed.
        await MoveAsync(Factory, ids[0], RequestStatuses.AgentProcessing, RequestStatuses.PendingApproval);
        (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await MoveAsync(Factory, ids[0], RequestStatuses.Rejected);

        (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Changing_max_open_requests_through_the_policy_api_changes_the_cap()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var (client, _, clubId) = await StudentRepAsync(factory);

        (await SetMaxOpenRequestsAsync(officer, 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await (await client.PostAsJsonAsync(Url, Body(clubId))).ShouldBeProblemAsync(409)).GetProperty("title").GetString()
            .Should().Be("You already have 1 open requests (the limit is 1)");

        (await SetMaxOpenRequestsAsync(officer, 2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Two_simultaneous_submits_at_cap_minus_one_give_exactly_one_201_and_one_409()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        (await SetMaxOpenRequestsAsync(officer, 2)).StatusCode.Should().Be(HttpStatusCode.OK);

        // A race only shows up sometimes, so try it with several users. Without the advisory lock some rounds give two 201s.
        for (var round = 0; round < 10; round++)
        {
            var (client, userId, clubId) = await StudentRepAsync(factory);
            (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Created);

            // A second client with the same user's token, so the two requests really run in parallel.
            var second = TestAuth.CreateClient(factory, Roles.Student, userId);
            var responses = await Task.WhenAll(
                client.PostAsJsonAsync(Url, Body(clubId, purpose: "Race A")),
                second.PostAsJsonAsync(Url, Body(clubId, purpose: "Race B")));

            responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict], $"round {round}");
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.BookingRequests.CountAsync(r => r.RequesterId == userId)).Should().Be(2);
        }
    }

    [Fact]
    public async Task Creating_a_request_writes_an_audit_row_with_property_names_only()
    {
        var (client, userId, clubId) = await StudentRepAsync(Factory);
        var secret = $"notes-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync(Url, Body(clubId, notes: secret));
        var id = (await created.ReadJsonAsync()).GetProperty("id").GetInt64().ToString();

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.AsNoTracking().SingleAsync(l => l.EntityType == nameof(BookingRequest) && l.EntityId == id);
        log.Action.Should().Be(AuditActions.Created);
        log.UserId.Should().Be(userId);
        log.DetailsJson.Should().Contain("Notes").And.NotContain(secret);
        // Across the whole table: no audit row may carry the Notes value.
        var leaks = await db.Database.SqlQuery<int>(
            $"""SELECT count(*)::int AS "Value" FROM "AuditLogs" WHERE "DetailsJson"::text LIKE {"%" + secret + "%"}""").SingleAsync();
        leaks.Should().Be(0);
    }

    private static Task<HttpResponseMessage> SetMaxOpenRequestsAsync(HttpClient officer, int value) =>
        officer.PutAsJsonAsync("/api/policy-settings",
            new { settings = new[] { new { key = PolicyKeys.MaxOpenRequests, value = value.ToString() } } });
}
