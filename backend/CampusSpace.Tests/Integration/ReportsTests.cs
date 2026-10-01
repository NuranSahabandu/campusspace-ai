using System.Net;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// Reports (UC22): GET /api/reports/utilization, /demand and /dashboard. Each test has its own database, sets its own
/// opening hours (not the seeded ones, so the numbers can only come from the policy) and checks hand-computed numbers.
/// The week used is Mon 2026-09-14 … Sun 2026-09-20.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ReportsTests(PostgresFixture fixture)
{
    private const string Utilization = "/api/reports/utilization";
    private const string Demand = "/api/reports/demand";
    private const string Dashboard = "/api/reports/dashboard";

    /// <summary>Mon–Fri 08:00–18:00 (10 h), Sat 09:00–13:00 (4 h), Sun closed: 54 h per room per week.</summary>
    private const string OpeningHours = """
        {"mon":{"open":"08:00","close":"18:00"},"tue":{"open":"08:00","close":"18:00"},"wed":{"open":"08:00","close":"18:00"},
         "thu":{"open":"08:00","close":"18:00"},"fri":{"open":"08:00","close":"18:00"},"sat":{"open":"09:00","close":"13:00"},"sun":null}
        """;

    private static readonly DateOnly Mon = new(2026, 9, 14);

    private static DateOnly D(int day) => new(2026, 9, day);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => CampusTime.At(D(day), new TimeOnly(hour, minute));

    private async Task<CustomWebApplicationFactory> FactoryAsync(TimeProvider? clock = null)
    {
        var factory = await fixture.CreateIsolatedFactoryAsync(clock);
        await QueryAsync(factory, db => db.PolicySettings.Where(p => p.Key == PolicyKeys.OpeningHours)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Value, OpeningHours)));
        return factory;
    }

    private static async Task<T> QueryAsync<T>(CustomWebApplicationFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<long> RoomAsync(CustomWebApplicationFactory factory, long buildingId, string code, bool isActive = true) =>
        FacilitiesTestData.CreateRoomAsync(factory, buildingId, code, RoomTypes.SeminarRoom, 30, [], isActive);

    private static Task<long> BookAsync(
        CustomWebApplicationFactory factory, long roomId, DateTimeOffset start, DateTimeOffset end, string status = BookingStatuses.Confirmed) =>
        BookingTestData.InsertBookingAsync(factory, roomId, start, end, status);

    private static Task<int> BlackoutAsync(CustomWebApplicationFactory factory, long roomId, long userId, DateTimeOffset start, DateTimeOffset end) =>
        QueryAsync(factory, db =>
        {
            db.RoomBlackouts.Add(new RoomBlackout
            {
                RoomId = roomId, TimeRange = CampusTime.UtcRange(start, end), Reason = "Maintenance", CreatedById = userId,
            });
            return db.SaveChangesAsync();
        });

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.ReadJsonAsync();
    }

    private static string Range(DateOnly from, DateOnly to) => $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

    private static (decimal Booked, decimal Available, double? Rate) Figures(JsonElement e)
    {
        var f = e.GetProperty("figures");
        return Numbers(f);
    }

    private static (decimal Booked, decimal Available, double? Rate) Numbers(JsonElement f) => (
        f.GetProperty("bookedHours").GetDecimal(), f.GetProperty("availableHours").GetDecimal(),
        f.GetProperty("utilization").ValueKind == JsonValueKind.Null ? null : f.GetProperty("utilization").GetDouble());

    private static JsonElement ByCode(JsonElement list, string code) =>
        list.EnumerateArray().Single(x => x.GetProperty("code").GetString() == code);

    // ---------- authorization and validation ----------

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Lecturer)]
    [InlineData(Roles.LabTechnician)]
    [InlineData(Roles.Admin)]
    public async Task Only_Facilities_Officers_read_reports(string role)
    {
        var client = TestAuth.CreateClient(fixture.Factory, role);
        foreach (var url in new[] { Utilization + Range(Mon, D(20)), Demand + Range(Mon, D(20)), Dashboard })
            await (await client.GetAsync(url)).ShouldBeProblemAsync(403);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var client = fixture.Factory.CreateClient();
        foreach (var url in new[] { Utilization + Range(Mon, D(20)), Demand + Range(Mon, D(20)), Dashboard })
            (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("?from=2026-09-20&to=2026-09-14", "From")]
    [InlineData("?from=2026-01-01&to=2027-01-02", "To")]   // 367 days
    [InlineData("?to=2026-09-20", "From")]
    [InlineData("?from=2026-09-14", "To")]
    [InlineData("?from=2026-09-14&to=not-a-date", "to")]
    public async Task A_bad_range_is_a_400_with_a_field_error(string query, string field)
    {
        var client = TestAuth.CreateClient(fixture.Factory, Roles.FacilitiesOfficer);
        foreach (var url in new[] { Utilization + query, Demand + query })
        {
            var problem = await (await client.GetAsync(url)).ShouldBeProblemAsync(400);
            problem.GetProperty("errors").EnumerateObject().Select(e => e.Name)
                .Should().Contain(n => string.Equals(n, field, StringComparison.OrdinalIgnoreCase), url);
        }
    }

    [Fact]
    public async Task A_range_of_exactly_366_days_is_accepted()
    {
        var client = TestAuth.CreateClient(fixture.Factory, Roles.FacilitiesOfficer);
        await GetJsonAsync(client, Utilization + "?from=2026-01-01&to=2027-01-01");
    }

    // ---------- utilization ----------

    [Fact]
    public async Task Utilization_clips_to_opening_hours_and_the_range_and_removes_blackouts()
    {
        await using var factory = await FactoryAsync();
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var x = await FacilitiesTestData.CreateBuildingAsync(factory, "BX");
        var y = await FacilitiesTestData.CreateBuildingAsync(factory, "BY");
        var r1 = await RoomAsync(factory, x, "R1");
        var r2 = await RoomAsync(factory, x, "R2");
        var r3 = await RoomAsync(factory, y, "R3");
        var r4 = await RoomAsync(factory, x, "R4", isActive: false);
        await RoomAsync(factory, x, "R5", isActive: false);

        // R1, week Mon 14 … Sun 20. Available 54 h − Thu blackouts 13–17 (two overlapping ones, counted once) = 50 h.
        await BookAsync(factory, r1, At(11, 17), At(14, 9));                                   // from before the range: Mon 08–09 = 1 h
        await BookAsync(factory, r1, At(14, 10), At(14, 12));                                  // 2 h
        await BookAsync(factory, r1, At(15, 17), At(16, 9), BookingStatuses.CheckedIn);        // across campus midnight: Tue 17–18 + Wed 08–09 = 2 h
        await BookAsync(factory, r1, At(16, 6), At(16, 7, 30), BookingStatuses.Completed);     // before opening: 0 h
        await BookAsync(factory, r1, At(17, 12), At(17, 14), BookingStatuses.Cancelled);       // cancelled: 0 h
        await BookAsync(factory, r1, At(17, 16), At(17, 18));                                  // 16–17 is blacked out: 1 h
        await BookAsync(factory, r1, At(19, 12), At(19, 15), BookingStatuses.Completed);       // Saturday closes at 13: 1 h
        await BookAsync(factory, r1, At(20, 10), At(20, 12));                                  // Sunday is closed: 0 h
        await BlackoutAsync(factory, r1, officerId, At(17, 13), At(17, 16));
        await BlackoutAsync(factory, r1, officerId, At(17, 15), At(17, 17));
        // R2: Fri 08–18 = 10 h of 54 h.
        await BookAsync(factory, r2, At(18, 8), At(18, 18));
        // R3: blacked out Mon 00:00 to Sun 00:00, so nothing is available.
        await BlackoutAsync(factory, r3, officerId, At(14, 0), At(20, 0));
        // R4 is inactive but has a booking: listed, not in the totals. R5 is inactive with none: not listed.
        await BookAsync(factory, r4, At(14, 10), At(14, 12));

        var report = await GetJsonAsync(officer, Utilization + Range(Mon, D(20)));

        var rooms = report.GetProperty("rooms");
        rooms.EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Equal("R1", "R2", "R4", "R3");
        Figures(ByCode(rooms, "R1")).Should().Be((7m, 50m, 0.14));
        Figures(ByCode(rooms, "R2")).Should().Be((10m, 54m, 0.1852));                          // 10 ÷ 54
        Figures(ByCode(rooms, "R3")).Should().Be((0m, 0m, (double?)null));
        Figures(ByCode(rooms, "R4")).Should().Be((2m, 54m, 0.037));                            // 2 ÷ 54 = 0.0370
        ByCode(rooms, "R4").GetProperty("isActive").GetBoolean().Should().BeFalse();

        // BX = R1 + R2 = 17 ÷ 104 = 0.1635 (the mean of 14% and 18.52% would be 16.26%). BY has no available time.
        var buildings = report.GetProperty("buildings");
        buildings.EnumerateArray().Select(b => b.GetProperty("code").GetString()).Should().Equal("BX", "BY");
        Figures(ByCode(buildings, "BX")).Should().Be((17m, 104m, 0.1635));
        ByCode(buildings, "BX").GetProperty("rooms").GetInt32().Should().Be(2);
        Figures(ByCode(buildings, "BY")).Should().Be((0m, 0m, (double?)null));
        Numbers(report.GetProperty("overall")).Should().Be((17m, 104m, 0.1635));

        // The range end clips too: Mon 14 … Tue 15 has 20 h; R1 has 1 + 2 + 1 (Tue 17–18 only) = 4 h.
        var twoDays = await GetJsonAsync(officer, Utilization + Range(Mon, D(15)));
        Figures(ByCode(twoDays.GetProperty("rooms"), "R1")).Should().Be((4m, 20m, 0.2));
    }

    [Fact]
    public async Task Utilization_counts_campus_days_not_UTC_days()
    {
        await using var factory = await FactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var room = await RoomAsync(factory, await FacilitiesTestData.CreateBuildingAsync(factory, "BC"), "C1");
        // Tue 15 08:00–09:00 campus is Tue 02:30–03:30 UTC; Mon 14 17:00–18:00 campus is Mon 11:30–12:30 UTC.
        await BookAsync(factory, room, At(15, 8), At(15, 9));
        await BookAsync(factory, room, At(14, 17), At(14, 18));

        var tuesday = await GetJsonAsync(officer, Utilization + Range(D(15), D(15)));
        Numbers(tuesday.GetProperty("overall")).Should().Be((1m, 10m, 0.1));
    }

    [Fact]
    public async Task An_empty_range_has_zero_hours_and_null_rates()
    {
        await using var factory = await FactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        await RoomAsync(factory, await FacilitiesTestData.CreateBuildingAsync(factory, "BE"), "E1");

        // A Sunday only: closed, so no available hours.
        var sunday = await GetJsonAsync(officer, Utilization + Range(D(20), D(20)));
        Numbers(sunday.GetProperty("overall")).Should().Be((0m, 0m, (double?)null));
        Figures(ByCode(sunday.GetProperty("rooms"), "E1")).Should().Be((0m, 0m, (double?)null));

        var demand = await GetJsonAsync(officer, Demand + Range(D(20), D(20)));
        demand.GetProperty("total").GetInt32().Should().Be(0);
        demand.GetProperty("byDay").GetArrayLength().Should().Be(1);
        demand.GetProperty("byHour").GetArrayLength().Should().Be(24);
        var approvals = demand.GetProperty("approvals");
        approvals.GetProperty("decided").GetInt32().Should().Be(0);
        approvals.GetProperty("approvalRate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---------- demand ----------

    [Fact]
    public async Task Demand_counts_submissions_by_campus_day_and_requested_hour()
    {
        await using var factory = await FactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var (_, student) = await TestAuth.CreateUserClientAsync(factory, Roles.Student);

        async Task SubmittedAsync(DateTimeOffset created, DateTimeOffset start)
        {
            var id = await BookingRequestTestData.InsertSubmittedAsync(factory, student, start);
            await QueryAsync(factory, db => db.BookingRequests.Where(r => r.Id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.CreatedAt, created.UtcDateTime)));
        }

        await SubmittedAsync(At(15, 0, 15), At(28, 14));     // 2026-09-14T18:45Z, but Tue 15 on campus; starts 14:xx
        await SubmittedAsync(At(14, 23, 45), At(28, 9, 30)); // Mon 14 on campus; starts 09:xx
        await SubmittedAsync(At(19, 10), At(29, 0, 30));     // Sat 19; starts 00:30 campus (the day before in UTC): hour 0
        await SubmittedAsync(At(21, 0), At(29, 10));         // Mon 21 00:00 campus: after the range

        var demand = await GetJsonAsync(officer, Demand + Range(Mon, D(20)));

        demand.GetProperty("total").GetInt32().Should().Be(3);
        demand.GetProperty("byDay").EnumerateArray()
            .Select(d => (d.GetProperty("date").GetString(), d.GetProperty("count").GetInt32()))
            .Should().Equal(
                ("2026-09-14", 1), ("2026-09-15", 1), ("2026-09-16", 0), ("2026-09-17", 0),
                ("2026-09-18", 0), ("2026-09-19", 1), ("2026-09-20", 0));
        var byHour = demand.GetProperty("byHour").EnumerateArray()
            .ToDictionary(h => h.GetProperty("hour").GetInt32(), h => h.GetProperty("count").GetInt32());
        byHour.Keys.Should().Equal(Enumerable.Range(0, 24));
        byHour.Where(h => h.Value > 0).Should().BeEquivalentTo(new Dictionary<int, int> { [0] = 1, [9] = 1, [14] = 1 });
    }

    [Fact]
    public async Task Approval_rate_is_officer_approved_over_officer_decided_and_the_rest_is_shown_apart()
    {
        // Every status change happens on Wed 16 (the factory's clock).
        await using var factory = await FactoryAsync(new FixedTimeProvider(At(16, 12)));
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var (_, student) = await TestAuth.CreateUserClientAsync(factory, Roles.Student);
        const string P = RequestStatuses.PendingApproval, A = RequestStatuses.AgentProcessing;

        async Task RequestAsync(params (string To, long? By)[] path)
        {
            var id = await BookingRequestTestData.InsertSubmittedAsync(factory, student);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var machine = scope.ServiceProvider.GetRequiredService<IRequestStateMachine>();
            var request = await db.BookingRequests.SingleAsync(r => r.Id == id);
            foreach (var (to, by) in path)
                machine.Transition(request, to, by, "test");
            await db.SaveChangesAsync();
        }

        await RequestAsync((A, null), (P, null), (RequestStatuses.Approved, officerId));                   // approved
        await RequestAsync((A, null), (P, null), (RequestStatuses.Approved, officerId),
            (RequestStatuses.Cancelled, student));                                                         // approved, then cancelled: still approved
        await RequestAsync((A, null), (P, null), (RequestStatuses.Rejected, officerId));                   // officer-rejected
        await RequestAsync((A, null), (P, null), (RequestStatuses.Rejected, null));                        // closed automatically
        await RequestAsync((A, null), (P, null), (RequestStatuses.Cancelled, officerId));                  // cancelled before a decision
        await RequestAsync((A, null), (RequestStatuses.AgentFailed, null), (A, null), (RequestStatuses.AgentFailed, null)); // failed twice: 1
        await RequestAsync((A, null), (P, null), (RequestStatuses.RevisionRequested, officerId), (A, null)); // revision requested

        var approvals = (await GetJsonAsync(officer, Demand + Range(Mon, D(20)))).GetProperty("approvals");
        approvals.GetProperty("approved").GetInt32().Should().Be(2);
        approvals.GetProperty("officerRejected").GetInt32().Should().Be(1);
        approvals.GetProperty("decided").GetInt32().Should().Be(3);
        approvals.GetProperty("approvalRate").GetDouble().Should().Be(0.6667);
        approvals.GetProperty("closedAutomatically").GetInt32().Should().Be(1);
        approvals.GetProperty("cancelledBeforeDecision").GetInt32().Should().Be(1);
        approvals.GetProperty("agentFailed").GetInt32().Should().Be(1);
        approvals.GetProperty("revisionsRequested").GetInt32().Should().Be(1);

        // Outcomes are counted on the date of the change: none happened on Thu 17.
        var thursday = (await GetJsonAsync(officer, Demand + Range(D(17), D(17)))).GetProperty("approvals");
        thursday.GetProperty("decided").GetInt32().Should().Be(0);
        thursday.GetProperty("approvalRate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---------- dashboard ----------

    [Fact]
    public async Task Dashboard_uses_campus_today_and_the_last_7_campus_days()
    {
        // 2026-09-15T19:00Z is Wed 16 00:30 on campus: today is the 16th although the UTC date is the 15th.
        await using var factory = await FactoryAsync(new FixedTimeProvider(At(16, 0, 30)));
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var (_, student) = await TestAuth.CreateUserClientAsync(factory, Roles.Student);
        var b = await FacilitiesTestData.CreateBuildingAsync(factory, "BD");
        var r1 = await RoomAsync(factory, b, "D1");
        var r2 = await RoomAsync(factory, b, "D2");

        await BookAsync(factory, r1, At(16, 8), At(16, 10));                                    // today, 2 h in hours
        await BookAsync(factory, r1, At(15, 22), At(15, 23, 30));                               // Tue (same UTC date as now): not today
        await BookAsync(factory, r2, At(15, 23), At(16, 0, 30));                                // Tue, runs into today: today
        await BookAsync(factory, r2, At(16, 1), At(16, 2), BookingStatuses.Cancelled);          // cancelled: not counted
        await BookingRequestTestData.MoveAsync(factory, await BookingRequestTestData.InsertSubmittedAsync(factory, student),
            RequestStatuses.AgentProcessing, RequestStatuses.PendingApproval);

        // Agent runs in the window: one reached the gate after 400 + 600 ms of steps, one failed with no steps.
        var gate = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var gateRequest = await BookingRequestTestData.InsertSubmittedAsync(factory, student);
        var failedRequest = await BookingRequestTestData.InsertSubmittedAsync(factory, student);
        var created = At(14, 9).UtcDateTime;
        await QueryAsync(factory, async db =>
        {
            db.AgentRuns.Add(new AgentRun
            {
                Id = gate, RequestId = gateRequest, RevisionNo = 1, Status = AgentRunStatuses.AwaitingApproval,
                Steps =
                [
                    new AgentStep { Sequence = 1, AgentName = "supervisor", Status = AgentStepStatuses.Succeeded, DurationMs = 400 },
                    new AgentStep { Sequence = 2, AgentName = "venue_matching", Status = AgentStepStatuses.Succeeded, DurationMs = 600 },
                ],
            });
            db.AgentRuns.Add(new AgentRun
            {
                Id = failed, RequestId = failedRequest, RevisionNo = 1,
                Status = AgentRunStatuses.Failed, FailureReason = "test failure",
            });
            await db.SaveChangesAsync();
            return await db.AgentRuns.Where(r => r.Id == gate || r.Id == failed).ExecuteUpdateAsync(u => u.SetProperty(r => r.CreatedAt, created));
        });

        var dash = await GetJsonAsync(officer, Dashboard);

        dash.GetProperty("today").GetString().Should().Be("2026-09-16");
        dash.GetProperty("from").GetString().Should().Be("2026-09-10");
        dash.GetProperty("to").GetString().Should().Be("2026-09-16");
        dash.GetProperty("pendingApprovals").GetInt32().Should().Be(1);
        dash.GetProperty("todayBookings").GetInt32().Should().Be(2);
        // Thu 10, Fri 11 = 20 h; Sat 12 = 4 h; Sun 13 = 0; Mon 14, Tue 15, Wed 16 = 30 h: 54 h per room, 108 h. Booked 2 h.
        Numbers(dash.GetProperty("utilization")).Should().Be((2m, 108m, 0.0185));
        Figures(ByCode(dash.GetProperty("utilizationByBuilding"), "BD")).Should().Be((2m, 108m, 0.0185));
        dash.GetProperty("bookingsPerDay").EnumerateArray()
            .Select(d => (d.GetProperty("date").GetString(), d.GetProperty("count").GetInt32()))
            .Should().Equal(
                ("2026-09-10", 0), ("2026-09-11", 0), ("2026-09-12", 0), ("2026-09-13", 0),
                ("2026-09-14", 0), ("2026-09-15", 2), ("2026-09-16", 1));

        var agent = dash.GetProperty("agent");
        agent.GetProperty("successRate").GetDouble().Should().Be(0.5);
        agent.GetProperty("reachedGate").GetInt32().Should().Be(1);
        agent.GetProperty("finished").GetInt32().Should().Be(2);
        agent.GetProperty("avgProcessingMs").GetInt32().Should().Be(1000);
        agent.GetProperty("processingRuns").GetInt32().Should().Be(1);

        // Exactly the 5.1 monitor's figures for the same range.
        var runs = (await GetJsonAsync(officer, "/api/agent-runs/metrics?from=2026-09-10&to=2026-09-16")).GetProperty("runs");
        agent.GetProperty("successRate").GetDouble().Should().Be(runs.GetProperty("successRate").GetDouble());
        agent.GetProperty("avgProcessingMs").GetInt32().Should().Be(runs.GetProperty("reachedGateProcessing").GetProperty("avgMs").GetInt32());
    }
}
