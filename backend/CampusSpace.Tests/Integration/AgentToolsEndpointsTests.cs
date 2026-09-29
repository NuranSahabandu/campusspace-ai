using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The agents' read-only tools (§9 internal routes) against a seeded database of their own: the plan's eval case, the
/// demo quote, the catalogs, request context and the offset rule. Every route leaves every table unchanged.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AgentToolsEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Url = AgentToolsAuth.Url;

    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _agent = null!;
    private long _a301, _e305, _seededRequest;

    /// <summary>Next week's Tuesday: an open weekday; nothing is booked on the seeded rooms.</summary>
    private static readonly DateOnly Tuesday = CampusTime.DateOf(BookingRequestTestData.Next(DayOfWeek.Tuesday, "00:00"));

    private static string At(string time, DateOnly? date = null) =>
        CampusTime.At(date ?? Tuesday, TimeOnly.Parse(time)).ToString("yyyy-MM-ddTHH:mm:sszzz");

    private static string Q(string value) => Uri.EscapeDataString(value);

    private static string EvalCaseUrl(string extra = "") =>
        $"{Url}/rooms/available?start={Q(At("14:00"))}&end={Q(At("17:00"))}&minCapacity=45&features=computers,projector{extra}";

    public async Task InitializeAsync()
    {
        _factory = await fixture.CreateIsolatedFactoryAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Seed.SeedAsync(db, "Test#Password1");
        _a301 = await db.Rooms.Where(r => r.Code == "A301").Select(r => r.Id).SingleAsync();
        _e305 = await db.Rooms.Where(r => r.Code == "E305").Select(r => r.Id).SingleAsync();
        _seededRequest = await db.BookingRequests.Where(r => r.Purpose == "Drama Society rehearsal").Select(r => r.Id).SingleAsync();
        _agent = AgentToolsAuth.CreateClient(_factory);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static object QuoteBody(long roomId, string role = RequesterRoles.Student, object[]? equipment = null,
        string? start = null, string? end = null) => new
    {
        roomId,
        start = start ?? At("14:00"),
        end = end ?? At("17:00"),
        requesterRole = role,
        equipment = equipment ?? [new { code = "MIC-WIRELESS", quantity = 2 }],
    };

    [Fact]
    public async Task Rooms_available_eval_case_returns_A301_and_N201_with_total()
    {
        var page = await (await _agent.GetAsync(EvalCaseUrl())).ReadJsonAsync();

        page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Equal("A301", "N201");
        page.GetProperty("total").GetInt32().Should().Be(2);
        page.GetProperty("page").GetInt32().Should().Be(1);
        page.GetProperty("pageSize").GetInt32().Should().Be(20);
        var a301 = page.GetProperty("items")[0];
        a301.GetProperty("capacity").GetInt32().Should().Be(48);
        a301.GetProperty("building").GetProperty("code").GetString().Should().Be("MB");
        a301.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("code").GetString())
            .Should().Contain(["computers", "projector"]);
    }

    [Fact]
    public async Task Rooms_available_keeps_total_when_the_page_cuts_results_off()
    {
        var page = await (await _agent.GetAsync(EvalCaseUrl("&pageSize=1"))).ReadJsonAsync();

        page.GetProperty("items").GetArrayLength().Should().Be(1);
        page.GetProperty("total").GetInt32().Should().Be(2);
        await (await _agent.GetAsync(EvalCaseUrl("&pageSize=101"))).ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task Rooms_available_rejects_unknown_features_and_off_grid_slots()
    {
        var unknown = await (await _agent.GetAsync(
            $"{Url}/rooms/available?start={Q(At("14:00"))}&end={Q(At("17:00"))}&minCapacity=45&features=teleporter")).ShouldBeProblemAsync(400);
        unknown.GetProperty("errors").GetProperty("Features")[0].GetString().Should().Contain("teleporter");

        var offGrid = await (await _agent.GetAsync(
            $"{Url}/rooms/available?start={Q(At("14:07"))}&end={Q(At("17:00"))}&minCapacity=45")).ShouldBeProblemAsync(400);
        offGrid.GetProperty("errors").TryGetProperty("Start", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Offset_is_required_on_every_time_parameter()
    {
        const string noOffset = "2030-10-08T14:00:00";
        var rooms = await (await _agent.GetAsync(
            $"{Url}/rooms/available?start={noOffset}&end={Q(At("17:00"))}&minCapacity=45")).ShouldBeProblemAsync(400);
        rooms.GetProperty("errors").GetProperty("Start")[0].GetString().Should().Contain("offset");

        var equipment = await (await _agent.GetAsync(
            $"{Url}/equipment/availability?codes=MIC-WIRELESS&start={Q(At("14:00"))}&end={noOffset}")).ShouldBeProblemAsync(400);
        equipment.GetProperty("errors").TryGetProperty("End", out _).Should().BeTrue();

        var quote = await (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(_a301, start: noOffset))).ShouldBeProblemAsync(400);
        quote.GetProperty("errors").TryGetProperty("Start", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Z_and_campus_offset_forms_of_the_same_instant_agree()
    {
        string Utc(string time) => CampusTime.At(Tuesday, TimeOnly.Parse(time)).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var zulu = await (await _agent.GetAsync(
            $"{Url}/rooms/available?start={Utc("14:00")}&end={Utc("17:00")}&minCapacity=45&features=computers,projector")).ReadJsonAsync();
        var campus = await (await _agent.GetAsync(EvalCaseUrl())).ReadJsonAsync();

        zulu.GetRawText().Should().Be(campus.GetRawText());
    }

    [Fact]
    public async Task Room_detail_returns_active_rooms_only()
    {
        var room = await (await _agent.GetAsync($"{Url}/rooms/{_a301}")).ReadJsonAsync();
        room.GetProperty("code").GetString().Should().Be("A301");
        room.GetProperty("features").GetArrayLength().Should().BeGreaterThan(0);

        await (await _agent.GetAsync($"{Url}/rooms/{_e305}")).ShouldBeProblemAsync(404);
        await (await _agent.GetAsync($"{Url}/rooms/999999")).ShouldBeProblemAsync(404);
    }

    [Fact]
    public async Task Quote_prices_the_demo_quote_by_role_and_saves_nothing()
    {
        var student = await (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(_a301))).ReadJsonAsync();
        student.GetProperty("total").GetDecimal().Should().Be(5500.00m);
        student.GetProperty("exempt").GetBoolean().Should().BeFalse();
        student.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        student.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("description").GetString())
            .Should().Equal("Computer lab A301, 3 h @ LKR 1,500", "Wireless microphone x2 @ LKR 500");

        var lecturer = await (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(_a301, RequesterRoles.Lecturer))).ReadJsonAsync();
        lecturer.GetProperty("total").GetDecimal().Should().Be(0.00m);
        lecturer.GetProperty("exempt").GetBoolean().Should().BeTrue();
        lecturer.GetProperty("discountReason").GetString().Should().Be(QuotationCalculator.LecturerExemptionReason);
    }

    [Fact]
    public async Task Quote_skips_room_builtin_lines()
    {
        object[] lines = [new { code = "MIC-WIRELESS", quantity = 2 }, new { code = "proj-portable", quantity = 0 }];

        var quote = await (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(_a301, equipment: lines))).ReadJsonAsync();

        quote.GetProperty("total").GetDecimal().Should().Be(5500.00m);
        quote.GetProperty("lines").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Quote_rejects_unknown_codes_rooms_roles_and_negative_quantities()
    {
        var unknown = await (await _agent.PostAsJsonAsync($"{Url}/quote",
            QuoteBody(_a301, equipment: [new { code = "NOPE-1", quantity = 1 }]))).ShouldBeProblemAsync(400);
        unknown.GetProperty("errors").GetProperty("Equipment")[0].GetString().Should().Contain("NOPE-1");

        await (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(999999))).ShouldBeProblemAsync(404);
        await (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(_e305))).ShouldBeProblemAsync(404);
        await (await _agent.PostAsJsonAsync($"{Url}/quote", new { roomId = _a301, start = At("14:00"), end = At("17:00") }))
            .ShouldBeProblemAsync(400);
        await (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(_a301, role: Roles.FacilitiesOfficer))).ShouldBeProblemAsync(400);
        await (await _agent.PostAsJsonAsync($"{Url}/quote",
            QuoteBody(_a301, equipment: [new { code = "MIC-WIRELESS", quantity = -1 }]))).ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task Catalogs_list_real_codes_with_fee_category_and_covering_feature()
    {
        var features = await (await _agent.GetAsync($"{Url}/catalog/features")).ReadJsonAsync();
        features.EnumerateArray().Select(f => f.GetProperty("code").GetString()).Should().Contain(["computers", "projector"]);

        var equipment = (await (await _agent.GetAsync($"{Url}/catalog/equipment")).ReadJsonAsync()).EnumerateArray().ToList();
        var projector = equipment.Single(t => t.GetProperty("code").GetString() == "PROJ-PORTABLE");
        projector.GetProperty("coveredByFeatureCode").GetString().Should().Be("projector");
        projector.GetProperty("feePerBooking").GetDecimal().Should().Be(1500m);
        var mic = equipment.Single(t => t.GetProperty("code").GetString() == "MIC-WIRELESS");
        mic.GetProperty("coveredByFeatureCode").ValueKind.Should().Be(JsonValueKind.Null);
        mic.GetProperty("category").GetString().Should().Be(EquipmentCategories.Audio);
        equipment.Select(t => t.GetProperty("code").GetString()).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task Equipment_availability_by_code_includes_the_covering_feature()
    {
        var result = (await (await _agent.GetAsync(
            $"{Url}/equipment/availability?codes=mic-wireless, PROJ-PORTABLE&start={Q(At("14:00"))}&end={Q(At("17:00"))}"))
            .ReadJsonAsync()).EnumerateArray().ToList();

        int serviceable;
        await using (var scope = _factory.Services.CreateAsyncScope())
            serviceable = await scope.ServiceProvider.GetRequiredService<AppDbContext>().EquipmentItems.CountAsync(i =>
                i.Type.Code == "MIC-WIRELESS"
                && (i.Status == EquipmentItemStatuses.Available || i.Status == EquipmentItemStatuses.OnLoan));

        result.Select(r => r.GetProperty("code").GetString()).Should().Equal("MIC-WIRELESS", "PROJ-PORTABLE");
        result[0].GetProperty("serviceable").GetInt32().Should().Be(serviceable).And.BePositive();
        result[0].GetProperty("reserved").GetInt32().Should().Be(0);
        result[0].GetProperty("available").GetInt32().Should().Be(serviceable);
        result[0].GetProperty("overAllocated").GetBoolean().Should().BeFalse();
        result[1].GetProperty("coveredByFeatureCode").GetString().Should().Be("projector");
    }

    [Fact]
    public async Task Equipment_availability_rejects_unknown_or_missing_codes()
    {
        var window = $"start={Q(At("14:00"))}&end={Q(At("17:00"))}";
        var unknown = await (await _agent.GetAsync($"{Url}/equipment/availability?codes=MIC-WIRELESS,NOPE-1&{window}"))
            .ShouldBeProblemAsync(400);
        unknown.GetProperty("errors").GetProperty("Codes")[0].GetString().Should().Contain("NOPE-1");
        await (await _agent.GetAsync($"{Url}/equipment/availability?codes=,&{window}")).ShouldBeProblemAsync(400);
        await (await _agent.GetAsync($"{Url}/equipment/availability?{window}")).ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task Substitutes_are_directional_by_code()
    {
        var subs = await (await _agent.GetAsync($"{Url}/equipment/substitutes/MIC-WIRELESS")).ReadJsonAsync();
        subs.EnumerateArray().Select(s => s.GetProperty("code").GetString()).Should().Equal("MIC-WIRED");

        await (await _agent.GetAsync($"{Url}/equipment/substitutes/NOPE-1")).ShouldBeProblemAsync(404);
    }

    [Fact]
    public async Task Request_context_has_the_request_club_and_counts_but_no_personal_data()
    {
        var response = await _agent.GetAsync($"{Url}/request-context/{_seededRequest}");
        var raw = await response.Content.ReadAsStringAsync();
        var context = await response.ReadJsonAsync();

        context.GetProperty("requestId").GetInt64().Should().Be(_seededRequest);
        context.GetProperty("status").GetString().Should().Be(RequestStatuses.Submitted);
        context.GetProperty("requesterRole").GetString().Should().Be(Roles.Student);
        context.GetProperty("club").GetProperty("name").GetString().Should().Be("Drama Society");
        context.GetProperty("club").GetProperty("isActive").GetBoolean().Should().BeTrue();
        context.GetProperty("club").GetProperty("requesterIsRepresentative").GetBoolean().Should().BeTrue();
        context.GetProperty("purpose").GetString().Should().Be("Drama Society rehearsal");
        context.GetProperty("attendees").GetInt32().Should().Be(30);
        context.GetProperty("requiredFeatures").EnumerateArray().Select(f => f.GetString()).Should().Equal("smart_board", "ac");
        context.GetProperty("equipment")[0].GetProperty("code").GetString().Should().Be("MIC-WIRED");
        context.GetProperty("equipment")[0].GetProperty("quantity").GetInt32().Should().Be(1);
        context.GetProperty("budgetLkr").GetDecimal().Should().Be(3000m);
        context.GetProperty("openRequestCount").GetInt32().Should().Be(0);
        context.GetProperty("maxOpenRequests").GetInt32().Should().BeGreaterThan(0);

        raw.Should().NotContain("@").And.NotContainEquivalentOf("email").And.NotContainEquivalentOf("nethmi")
            .And.NotContainEquivalentOf("requesterId").And.NotContainEquivalentOf("password");
    }

    [Fact]
    public async Task Request_context_counts_the_requesters_other_open_requests_only()
    {
        var (client, _, clubId) = await BookingRequestTestData.StudentRepAsync(_factory);
        async Task<long> SubmitAsync(int weekdaysAhead) => (await (await client.PostAsJsonAsync(BookingRequestTestData.Url,
            BookingRequestTestData.Body(clubId, start: BookingRequestTestData.FutureStart(weekdaysAhead)))).ReadJsonAsync())
            .GetProperty("id").GetInt64();
        var closed = await SubmitAsync(5);
        await BookingRequestTestData.MoveAsync(_factory, closed, RequestStatuses.PendingApproval, RequestStatuses.Rejected);
        var other = await SubmitAsync(6);
        var target = await SubmitAsync(7);

        var context = await (await _agent.GetAsync($"{Url}/request-context/{target}")).ReadJsonAsync();

        context.GetProperty("openRequestCount").GetInt32().Should().Be(1, "only {0} is open besides this one", other);
        await (await _agent.GetAsync($"{Url}/request-context/999999")).ShouldBeProblemAsync(404);
    }

    [Fact]
    public async Task Policy_returns_the_full_snapshot()
    {
        var policy = await (await _agent.GetAsync($"{Url}/policy")).ReadJsonAsync();

        policy.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(PolicyKeys.All);
        policy.GetProperty(PolicyKeys.OpeningHours).GetProperty("sun").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Every_internal_route_leaves_every_table_unchanged()
    {
        var before = await RowCountsAsync();
        var window = $"start={Q(At("14:00"))}&end={Q(At("17:00"))}";
        string[] gets =
        [
            $"{Url}/request-context/{_seededRequest}", $"{Url}/request-context/999999",
            $"{Url}/catalog/features", $"{Url}/catalog/equipment",
            EvalCaseUrl(), $"{Url}/rooms/available?{window}&minCapacity=45&features=teleporter",
            $"{Url}/rooms/{_a301}", $"{Url}/rooms/999999",
            $"{Url}/equipment/availability?codes=MIC-WIRELESS&{window}", $"{Url}/equipment/availability?codes=NOPE&{window}",
            $"{Url}/equipment/substitutes/MIC-WIRELESS", $"{Url}/equipment/substitutes/NOPE",
            $"{Url}/policy",
        ];
        foreach (var url in gets)
            ((int)(await _agent.GetAsync(url)).StatusCode).Should().BeLessThan(500, url);
        foreach (var role in RequesterRoles.All)
            (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(_a301, role))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _agent.PostAsJsonAsync($"{Url}/quote", QuoteBody(999999))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.CreateClient().GetAsync($"{Url}/policy")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await RowCountsAsync()).Should().Equal(before);
    }

    private async Task<Dictionary<string, long>> RowCountsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var connection = (NpgsqlConnection)scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' " +
            "AND table_name <> '__EFMigrationsHistory' ORDER BY table_name", connection))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));

        tables.Should().Contain(["AgentRuns", "AuditLogs", "Quotations", "BookingRequests"]);
        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", connection);
            counts[table] = (long)(await count.ExecuteScalarAsync())!;
        }
        return counts;
    }
}
