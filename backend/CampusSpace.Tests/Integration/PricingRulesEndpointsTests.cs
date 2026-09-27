using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// Pricing rules API. Tests on the shared database give every rule a unique far-away date (PricingTestData), so they
/// never collide. Tests that assert statuses or /current use a database of their own.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PricingRulesEndpointsTests(PostgresFixture fixture)
{
    private const string Url = "/api/pricing-rules";
    private const string InEffect = "Rules already in effect can't be changed; add a new rule with a later start date";

    private static object NewRule(DateOnly validFrom, decimal rate = 750m, bool exempt = false,
        string roomType = RoomTypes.SeminarRoom, string role = RequesterRoles.Student) =>
        new { roomType, requesterRole = role, hourlyRate = rate, isExempt = exempt, validFrom = validFrom.ToString("yyyy-MM-dd") };

    private static async Task<HttpClient> OfficerAsync(CustomWebApplicationFactory factory) =>
        (await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer)).Client;

    private static string ErrorOn(JsonElement problem, string field) => problem.GetProperty("errors").GetProperty(field)[0].GetString()!;

    [Fact]
    public async Task Only_facilities_officers_can_use_pricing_rules()
    {
        var anonymous = fixture.Factory.CreateClient();
        await (await anonymous.GetAsync(Url)).ShouldBeProblemAsync(401);
        await (await anonymous.GetAsync($"{Url}/current")).ShouldBeProblemAsync(401);

        foreach (var role in new[] { Roles.Student, Roles.Lecturer, Roles.LabTechnician, Roles.Admin })
        {
            var client = TestAuth.CreateClient(fixture.Factory, role);
            (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
            (await client.GetAsync($"{Url}/current")).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
            (await client.PostAsJsonAsync(Url, NewRule(PricingTestData.UniqueFutureDate()))).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, role);
            (await client.DeleteAsync($"{Url}/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
        }

        var officer = TestAuth.CreateClient(fixture.Factory, Roles.FacilitiesOfficer);
        (await officer.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await officer.GetAsync($"{Url}/current")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Officer_creates_edits_and_deletes_a_scheduled_rule()
    {
        var officer = await OfficerAsync(fixture.Factory);
        var validFrom = PricingTestData.UniqueFutureDate();

        var created = await officer.PostAsJsonAsync(Url, NewRule(validFrom, 750.5m));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.ReadJsonAsync();
        var id = body.GetProperty("id").GetInt64();
        created.Headers.Location!.AbsolutePath.Should().Be($"{Url}/{id}");
        body.GetProperty("hourlyRate").GetDecimal().Should().Be(750.5m);
        body.GetProperty("validFrom").GetString().Should().Be(validFrom.ToString("yyyy-MM-dd"));
        body.GetProperty("status").GetString().Should().Be(PricingRuleStatuses.Scheduled);

        var later = validFrom.AddDays(1);
        var updated = await officer.PutAsJsonAsync($"{Url}/{id}", NewRule(later, 0m, exempt: true));

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await updated.ReadJsonAsync();
        after.GetProperty("isExempt").GetBoolean().Should().BeTrue();
        after.GetProperty("hourlyRate").GetDecimal().Should().Be(0m);
        after.GetProperty("validFrom").GetString().Should().Be(later.ToString("yyyy-MM-dd"));

        (await officer.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await officer.GetAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_rule_starting_in_the_past_returns_400_on_ValidFrom()
    {
        var officer = await OfficerAsync(fixture.Factory);

        var past = await officer.PostAsJsonAsync(Url, NewRule(PricingTestData.Today.AddDays(-1)));

        ErrorOn(await past.ShouldBeProblemAsync(400), "ValidFrom").Should().Contain("past");
    }

    [Fact]
    public async Task Duplicate_room_type_role_and_date_returns_409()
    {
        var officer = await OfficerAsync(fixture.Factory);
        var validFrom = PricingTestData.UniqueFutureDate();
        (await officer.PostAsJsonAsync(Url, NewRule(validFrom))).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await officer.PostAsJsonAsync(Url, NewRule(validFrom, 999m));

        (await duplicate.ShouldBeProblemAsync(409)).GetProperty("title").GetString()
            .Should().Be("A rule for this room type, role and date already exists");
    }

    [Fact]
    public async Task Moving_a_scheduled_rule_onto_another_rules_date_returns_409()
    {
        var officer = await OfficerAsync(fixture.Factory);
        var first = PricingTestData.UniqueFutureDate();
        await officer.PostAsJsonAsync(Url, NewRule(first));
        var second = await (await officer.PostAsJsonAsync(Url, NewRule(first.AddDays(1)))).ReadJsonAsync();

        var moved = await officer.PutAsJsonAsync($"{Url}/{second.GetProperty("id").GetInt64()}", NewRule(first));

        await moved.ShouldBeProblemAsync(409);
    }

    [Theory]
    [InlineData(100, true, "HourlyRate")]
    [InlineData(10.555, false, "HourlyRate")]
    [InlineData(-1, false, "HourlyRate")]
    public async Task Bad_rates_return_400_on_HourlyRate(decimal rate, bool exempt, string field)
    {
        var officer = await OfficerAsync(fixture.Factory);

        var response = await officer.PostAsJsonAsync(Url, NewRule(PricingTestData.UniqueFutureDate(), rate, exempt));

        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_room_type_or_role_returns_400()
    {
        var officer = await OfficerAsync(fixture.Factory);
        var date = PricingTestData.UniqueFutureDate();

        var badType = await officer.PostAsJsonAsync(Url, NewRule(date, roomType: "Gym"));
        var badRole = await officer.PostAsJsonAsync(Url, NewRule(date, role: Roles.Admin));

        (await badType.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("RoomType", out _).Should().BeTrue();
        (await badRole.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("RequesterRole", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_rule_in_effect_cannot_be_edited_or_deleted()
    {
        var officer = await OfficerAsync(fixture.Factory);
        var id = await PricingTestData.InsertAsync(fixture.Factory, RoomTypes.ComputerLab, RequesterRoles.Student,
            PricingTestData.UniquePastDate(), 1500m);

        var edit = await officer.PutAsJsonAsync($"{Url}/{id}", NewRule(PricingTestData.UniqueFutureDate(), 1, roomType: RoomTypes.ComputerLab));
        var delete = await officer.DeleteAsync($"{Url}/{id}");

        ErrorOn(await edit.ShouldBeProblemAsync(400), "ValidFrom").Should().Be(InEffect);
        ErrorOn(await delete.ShouldBeProblemAsync(400), "ValidFrom").Should().Be(InEffect);
        (await officer.GetAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_rule_starting_today_is_already_in_effect()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var officer = await OfficerAsync(factory);

        var created = await (await officer.PostAsJsonAsync(Url, NewRule(PricingTestData.Today))).ReadJsonAsync();

        created.GetProperty("status").GetString().Should().Be(PricingRuleStatuses.Current);
        await (await officer.DeleteAsync($"{Url}/{created.GetProperty("id").GetInt64()}")).ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task Missing_rules_return_404()
    {
        var officer = await OfficerAsync(fixture.Factory);

        (await officer.GetAsync($"{Url}/999999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await officer.PutAsJsonAsync($"{Url}/999999999", NewRule(PricingTestData.UniqueFutureDate()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await officer.DeleteAsync($"{Url}/999999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_shows_statuses_and_hides_superseded_rules_unless_history_is_asked_for()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var today = PricingTestData.Today;
        var old = await PricingTestData.InsertAsync(factory, RoomTypes.ComputerLab, RequesterRoles.Student, today.AddYears(-1), 1200m);
        var current = await PricingTestData.InsertAsync(factory, RoomTypes.ComputerLab, RequesterRoles.Student, today.AddDays(-10), 1500m);
        var scheduled = await PricingTestData.InsertAsync(factory, RoomTypes.ComputerLab, RequesterRoles.Student, today.AddMonths(2), 1800m);
        var other = await PricingTestData.InsertAsync(factory, RoomTypes.ComputerLab, RequesterRoles.Lecturer, today.AddYears(-1), 0m, exempt: true);
        var officer = await OfficerAsync(factory);

        var defaultList = await (await officer.GetAsync(Url)).ReadJsonAsync();
        var history = await (await officer.GetAsync($"{Url}?includeHistory=true&sort=-validFrom")).ReadJsonAsync();

        static Dictionary<long, string> Statuses(JsonElement page) => page.GetProperty("items").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetInt64(), r => r.GetProperty("status").GetString()!);
        Statuses(defaultList).Should().BeEquivalentTo(new Dictionary<long, string>
        {
            [current] = PricingRuleStatuses.Current, [scheduled] = PricingRuleStatuses.Scheduled, [other] = PricingRuleStatuses.Current,
        });
        defaultList.GetProperty("total").GetInt32().Should().Be(3);
        Statuses(history)[old].Should().Be(PricingRuleStatuses.Superseded);
        // Same date: ties sort by room type, then role (Lecturer before Student).
        history.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetInt64())
            .Should().Equal(scheduled, current, other, old);

        var filtered = await (await officer.GetAsync($"{Url}?requesterRole=Lecturer&roomType=ComputerLab")).ReadJsonAsync();
        Statuses(filtered).Keys.Should().Equal(other);
        await (await officer.GetAsync($"{Url}?sort=price")).ShouldBeProblemAsync(400);
        await (await officer.GetAsync($"{Url}?requesterRole=Admin")).ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task Current_lists_every_room_type_and_role_with_nulls_for_gaps()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var today = PricingTestData.Today;
        await PricingTestData.InsertAsync(factory, RoomTypes.ComputerLab, RequesterRoles.Student, today.AddYears(-1), 1200m);
        var current = await PricingTestData.InsertAsync(factory, RoomTypes.ComputerLab, RequesterRoles.Student, today, 1500m);
        await PricingTestData.InsertAsync(factory, RoomTypes.Auditorium, RequesterRoles.Student, today.AddDays(1), 3000m);
        var officer = await OfficerAsync(factory);

        var rows = (await (await officer.GetAsync($"{Url}/current")).ReadJsonAsync()).EnumerateArray().ToList();

        rows.Should().HaveCount(RoomTypes.All.Count * RequesterRoles.All.Count);
        var lab = rows.Single(r => r.GetProperty("roomType").GetString() == RoomTypes.ComputerLab
            && r.GetProperty("requesterRole").GetString() == RequesterRoles.Student);
        lab.GetProperty("id").GetInt64().Should().Be(current);
        lab.GetProperty("hourlyRate").GetDecimal().Should().Be(1500m);
        // Scheduled only: still a gap today.
        var auditorium = rows.Single(r => r.GetProperty("roomType").GetString() == RoomTypes.Auditorium
            && r.GetProperty("requesterRole").GetString() == RequesterRoles.Student);
        auditorium.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        auditorium.GetProperty("hourlyRate").ValueKind.Should().Be(JsonValueKind.Null);
        rows.Count(r => r.GetProperty("id").ValueKind == JsonValueKind.Null).Should().Be(7);
    }

    [Fact]
    public async Task Creating_a_rule_writes_a_names_only_audit_row()
    {
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.FacilitiesOfficer);

        var created = await (await officer.PostAsJsonAsync(Url, NewRule(PricingTestData.UniqueFutureDate(), 1234m))).ReadJsonAsync();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityType == nameof(PricingRule) && a.EntityId == created.GetProperty("id").GetInt64().ToString());
        log.Action.Should().Be(AuditActions.Created);
        log.UserId.Should().Be(officerId);
        log.DetailsJson.Should().Contain("HourlyRate").And.NotContain("1234");
    }
}
