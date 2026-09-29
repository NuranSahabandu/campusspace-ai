using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
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
    public async Task Student_representative_submits_and_gets_202_with_the_request_now_AgentProcessing_and_a_running_agent_run()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(Factory);
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        var (client, userId, clubId) = await StudentRepAsync(Factory);
        var start = FutureStart(weekdaysAhead: 25, hour: 14);

        var response = await client.PostAsJsonAsync(Url, Body(clubId, purpose: "  Robotics workshop  ", start: start,
            features: ["Computers", " projector ", "computers"], equipment: [Line(type.Id, 2)], notes: "prefer near the main building"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.ReadJsonAsync();
        var id = body.GetProperty("id").GetInt64();
        response.Headers.Location!.AbsolutePath.Should().Be($"{Url}/{id}");
        body.GetProperty("status").GetString().Should().Be(RequestStatuses.AgentProcessing);
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
        history.GetArrayLength().Should().Be(2);
        history[0].GetProperty("fromStatus").ValueKind.Should().Be(JsonValueKind.Null);
        history[0].GetProperty("toStatus").GetString().Should().Be(RequestStatuses.Submitted);
        history[1].GetProperty("fromStatus").GetString().Should().Be(RequestStatuses.Submitted);
        history[1].GetProperty("toStatus").GetString().Should().Be(RequestStatuses.AgentProcessing);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.BookingRequests.AsNoTracking().Include(r => r.EquipmentLines).Include(r => r.StatusHistory)
            .SingleAsync(r => r.Id == id);
        saved.RequesterId.Should().Be(userId);
        saved.RequestedStart.Should().Be(start.UtcDateTime);
        saved.EquipmentLines.Should().ContainSingle(l => l.TypeId == type.Id && l.Quantity == 2);
        saved.StatusHistory.Should().ContainSingle(h => h.FromStatus == null && h.ToStatus == RequestStatuses.Submitted && h.ChangedById == userId);
        saved.StatusHistory.Should().ContainSingle(h => h.FromStatus == RequestStatuses.Submitted
            && h.ToStatus == RequestStatuses.AgentProcessing && h.ChangedById == userId);

        // AgentRun #1 was created with the request and started at once (the shared fake accepts every start).
        var run = await db.AgentRuns.AsNoTracking().SingleAsync(r => r.RequestId == id);
        run.RevisionNo.Should().Be(1);
        run.Status.Should().Be(AgentRunStatuses.Running);
        run.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        Factory.AgentClient.Calls.Should().Contain(("start", run.Id, null));
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
        created.StatusCode.Should().Be(HttpStatusCode.Accepted);
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

        var past = Body(clubId, start: PastStart());
        (await ErrorAsync(await client.PostAsJsonAsync(Url, past), "RequestedStart")).Should().Be("Start must be in the future.");
    }

    [Fact]
    public async Task Several_field_errors_are_reported_together_and_unknown_features_are_listed()
    {
        await FacilitiesTestData.EnsureFeaturesAsync(Factory);
        var (client, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);

        var response = await client.PostAsJsonAsync(Url, Body(clubId: null, start: PastStart(),
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
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
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
            created.StatusCode.Should().Be(HttpStatusCode.Accepted);
            ids.Add((await created.ReadJsonAsync()).GetProperty("id").GetInt64());
        }

        var blocked = await client.PostAsJsonAsync(Url, Body(clubId));
        (await blocked.ShouldBeProblemAsync(409)).GetProperty("title").GetString()
            .Should().Be("You already have 3 open requests (the limit is 3)");

        // PendingApproval is still open; Rejected is closed.
        await MoveAsync(Factory, ids[0], RequestStatuses.PendingApproval);
        (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await MoveAsync(Factory, ids[0], RequestStatuses.Rejected);

        (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Changing_max_open_requests_through_the_policy_api_changes_the_cap()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var (client, _, clubId) = await StudentRepAsync(factory);

        (await SetMaxOpenRequestsAsync(officer, 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await (await client.PostAsJsonAsync(Url, Body(clubId))).ShouldBeProblemAsync(409)).GetProperty("title").GetString()
            .Should().Be("You already have 1 open requests (the limit is 1)");

        (await SetMaxOpenRequestsAsync(officer, 2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Two_simultaneous_submits_at_cap_minus_one_give_exactly_one_202_and_one_409()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        (await SetMaxOpenRequestsAsync(officer, 2)).StatusCode.Should().Be(HttpStatusCode.OK);

        // A race only shows up sometimes, so try it with several users. Without the advisory lock some rounds give two 202s.
        for (var round = 0; round < 10; round++)
        {
            var (client, userId, clubId) = await StudentRepAsync(factory);
            (await client.PostAsJsonAsync(Url, Body(clubId))).StatusCode.Should().Be(HttpStatusCode.Accepted);

            // A second client with the same user's token, so the two requests really run in parallel.
            var second = TestAuth.CreateClient(factory, Roles.Student, userId);
            var responses = await Task.WhenAll(
                client.PostAsJsonAsync(Url, Body(clubId, purpose: "Race A")),
                second.PostAsJsonAsync(Url, Body(clubId, purpose: "Race B")));

            responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Accepted, HttpStatusCode.Conflict], $"round {round}");
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

    [Fact]
    public async Task Each_booking_slot_rule_is_a_400_on_the_right_field_with_the_mobile_message()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);

        async Task ExpectAsync(DateTimeOffset start, double hours, string field, string message) =>
            (await ErrorAsync(await client.PostAsJsonAsync(Url, Body(clubId, start: start, hours: hours)), field))
                .Should().Be(message, $"{start:ddd HH:mm} for {hours} h");

        await ExpectAsync(Next(DayOfWeek.Sunday, "10:00"), 3, "RequestedStart", "The campus is closed on Sundays");
        await ExpectAsync(Next(DayOfWeek.Monday, "14:15"), 2, "RequestedStart", "Must be on a 30-minute boundary");
        await ExpectAsync(Next(DayOfWeek.Monday, "14:00"), 2.25, "RequestedEnd", "Must be on a 30-minute boundary");
        await ExpectAsync(Next(DayOfWeek.Saturday, "07:30"), 2, "RequestedStart", "Opens at 08:00 on Saturdays");
        await ExpectAsync(Next(DayOfWeek.Saturday, "16:00"), 1, "RequestedStart", "Closes at 16:00 on Saturdays");
        await ExpectAsync(Next(DayOfWeek.Saturday, "15:00"), 2, "RequestedEnd", "Must end by 16:00 on Saturdays");
        await ExpectAsync(Next(DayOfWeek.Monday, "09:00"), 8.5, "RequestedEnd", "Bookings can be at most 8 hours");
        await ExpectAsync(Next(DayOfWeek.Monday, "19:00"), 6, "RequestedEnd", "Must end on the same day as the start");

        // The limits themselves are fine: Saturday 08:00–16:00 is 8 hours, from opening to closing.
        (await client.PostAsJsonAsync(Url, Body(clubId, start: Next(DayOfWeek.Saturday, "08:00"), hours: 8)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    /// <summary>Wednesday 2031-03-12 10:00 campus time: the frozen "now" of the timing tests.</summary>
    private static readonly DateOnly FrozenToday = new(2031, 3, 12);

    private static DateTimeOffset Frozen(int daysLater, string time) => CampusTime.At(FrozenToday.AddDays(daysLater), TimeOnly.Parse(time));

    [Fact]
    public async Task Lead_time_uses_the_current_policy_and_the_boundary_itself_is_allowed()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Frozen(0, "10:00")));
        var (client, _, clubId) = await StudentRepAsync(factory);

        // Thursday 10:00 is 24 h away; the seeded min_lead_time_hours is 48.
        (await ErrorAsync(await client.PostAsJsonAsync(Url, Body(clubId, start: Frozen(1, "10:00"))), "RequestedStart"))
            .Should().Be("Must start at least 48 hours from now");
        // Friday 10:00 is exactly 48 h away.
        (await client.PostAsJsonAsync(Url, Body(clubId, start: Frozen(2, "10:00"))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task The_advance_window_depends_on_the_requester_role()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Frozen(0, "10:00")));
        var (student, _, clubId) = await StudentRepAsync(factory);
        var (lecturer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.Lecturer);

        // Monday 2031-05-12 is 61 days ahead: past the student's 60, inside the lecturer's 90.
        (await ErrorAsync(await student.PostAsJsonAsync(Url, Body(clubId, start: Frozen(61, "10:00"))), "RequestedStart"))
            .Should().Be("Can be booked at most 60 days ahead");
        (await student.PostAsJsonAsync(Url, Body(clubId, start: Frozen(58, "10:00"))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await lecturer.PostAsJsonAsync(Url, Body(clubId: null, start: Frozen(61, "10:00"))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task When_the_agent_service_is_down_submit_still_returns_202_and_leaves_the_run_Queued()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        factory.AgentClient.Start = (_, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());
        var (client, _, clubId) = await StudentRepAsync(factory);

        var response = await client.PostAsJsonAsync(Url, Body(clubId));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
        var run = await RunAsync(factory, id);
        run.Status.Should().Be(AgentRunStatuses.Queued);
        run.StartedAt.Should().BeNull();
        run.RevisionNo.Should().Be(1);
    }

    [Fact]
    public async Task A_hanging_agent_service_does_not_slow_down_submit_past_the_inline_start_timeout()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        factory.AgentClient.Start = async (thread, _, ct) =>
        {
            // Longer than AgentService:InlineStartTimeoutSeconds (3); honours the token like HttpClient does.
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return FakeAgentClient.Accepted(thread);
        };
        var (client, _, clubId) = await StudentRepAsync(factory);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync(Url, Body(clubId));
        watch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6));
        watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2.9));
        (await RunAsync(factory, (await response.ReadJsonAsync()).GetProperty("id").GetInt64())).Status
            .Should().Be(AgentRunStatuses.Queued);
    }

    [Fact]
    public async Task An_agent_service_that_already_has_the_thread_counts_as_started()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        factory.AgentClient.Start = (_, _, _) => Task.FromResult(
            new AgentCallResult<AgentWorkflowAccepted>(AgentCallOutcome.AlreadyExists));
        var (client, _, clubId) = await StudentRepAsync(factory);

        var response = await client.PostAsJsonAsync(Url, Body(clubId));

        (await RunAsync(factory, (await response.ReadJsonAsync()).GetProperty("id").GetInt64())).Status
            .Should().Be(AgentRunStatuses.Running);
    }

    private static async Task<AgentRun> RunAsync(CustomWebApplicationFactory factory, long requestId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentRuns.AsNoTracking().SingleAsync(r => r.RequestId == requestId);
    }

    private static Task<HttpResponseMessage> SetMaxOpenRequestsAsync(HttpClient officer, int value) =>
        officer.PutAsJsonAsync("/api/policy-settings",
            new { settings = new[] { new { key = PolicyKeys.MaxOpenRequests, value = value.ToString() } } });
}
