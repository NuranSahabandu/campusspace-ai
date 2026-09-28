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

/// <summary>Policy settings API. Every test changes policy, so each one runs on a database of its own.</summary>
[Collection(PostgresCollection.Name)]
public class PolicySettingsEndpointsTests(PostgresFixture fixture)
{
    private const string Url = "/api/policy-settings";

    private static object Body(params (string Key, string Value)[] settings) =>
        new { settings = settings.Select(s => new { key = s.Key, value = s.Value }) };

    private static Dictionary<string, JsonElement> ByKey(JsonElement list) =>
        list.EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!, s => s);

    private static async Task<List<AuditLog>> PolicyAuditsAsync(CustomWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == nameof(PolicySetting)).OrderBy(a => a.Id).ToListAsync();
    }

    [Fact]
    public async Task Officer_reads_every_setting_and_others_read_only_the_public_values()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();

        await (await factory.CreateClient().GetAsync($"{Url}/public")).ShouldBeProblemAsync(401);
        foreach (var role in new[] { Roles.Student, Roles.Lecturer, Roles.LabTechnician, Roles.Admin })
        {
            var client = TestAuth.CreateClient(factory, role);
            (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
            (await client.GetAsync($"{Url}/public")).StatusCode.Should().Be(HttpStatusCode.OK, role);
        }

        var officer = TestAuth.CreateClient(factory, Roles.FacilitiesOfficer);
        var list = await (await officer.GetAsync(Url)).ReadJsonAsync();
        list.EnumerateArray().Select(s => s.GetProperty("key").GetString()).Should().Equal(PolicyKeys.All);
        var ratio = ByKey(list)[PolicyKeys.MaxCapacityRatio];
        ratio.GetProperty("value").GetString().Should().Be("3");
        ratio.GetProperty("valueType").GetString().Should().Be(PolicyValueTypes.Decimal);
        ratio.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        ratio.GetProperty("updatedByName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Public_values_are_typed_and_carry_no_editor_details()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();

        var values = await (await TestAuth.CreateClient(factory, Roles.Student).GetAsync($"{Url}/public")).ReadJsonAsync();

        values.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(PolicyKeys.All);
        values.GetProperty(PolicyKeys.MinLeadTimeHours).GetInt32().Should().Be(48);
        values.GetProperty(PolicyKeys.MaxAdvanceDaysStudent).GetInt32().Should().Be(60);
        values.GetProperty(PolicyKeys.MaxAdvanceDaysLecturer).GetInt32().Should().Be(90);
        values.GetProperty(PolicyKeys.MaxCapacityRatio).GetDecimal().Should().Be(3m);
        values.GetProperty(PolicyKeys.OpeningHours).GetProperty("mon").GetProperty("open").GetString().Should().Be("08:00");
        values.GetProperty(PolicyKeys.OpeningHours).GetProperty("sun").ValueKind.Should().Be(JsonValueKind.Null);
        values.GetRawText().Should().NotContain("updatedBy").And.NotContain("description");
    }

    [Fact]
    public async Task Only_officers_can_change_policy()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();

        foreach (var role in new[] { Roles.Student, Roles.LabTechnician, Roles.Admin })
            (await TestAuth.CreateClient(factory, role).PutAsJsonAsync(Url, Body((PolicyKeys.MaxCapacityRatio, "2"))))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
    }

    [Fact]
    public async Task Officer_saves_every_key_and_the_public_values_change_immediately()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var hours = PolicySettingDefaults.OpeningHoursJson.Replace("\"sun\":null", "\"sun\":{\"open\":\"10:00\",\"close\":\"14:00\"}");

        var response = await officer.PutAsJsonAsync(Url, Body(
            (PolicyKeys.OpeningHours, hours), (PolicyKeys.MinLeadTimeHours, "24"), (PolicyKeys.MaxAdvanceDaysStudent, "30"),
            (PolicyKeys.MaxAdvanceDaysLecturer, "120"), (PolicyKeys.MaxDurationHours, "6"), (PolicyKeys.MaxCapacityRatio, "2.5"),
            (PolicyKeys.SlotGranularityMinutes, "60"), (PolicyKeys.FreeCancellationHours, "12"), (PolicyKeys.MaxOpenRequests, "5"),
            (PolicyKeys.CheckoutWindowMinutes, "45")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = ByKey(await response.ReadJsonAsync());
        saved[PolicyKeys.MaxCapacityRatio].GetProperty("value").GetString().Should().Be("2.5");
        saved.Values.Should().OnlyContain(s => s.GetProperty("updatedByName").GetString()!.StartsWith("Test FacilitiesOfficer"));
        saved.Values.Should().OnlyContain(s => s.GetProperty("updatedAt").GetDateTime() > DateTime.UtcNow.AddMinutes(-5));

        var values = await (await TestAuth.CreateClient(factory, Roles.Student).GetAsync($"{Url}/public")).ReadJsonAsync();
        values.GetProperty(PolicyKeys.MaxCapacityRatio).GetDecimal().Should().Be(2.5m);
        values.GetProperty(PolicyKeys.SlotGranularityMinutes).GetInt32().Should().Be(60);
        values.GetProperty(PolicyKeys.CheckoutWindowMinutes).GetInt32().Should().Be(45);
        values.GetProperty(PolicyKeys.OpeningHours).GetProperty("sun").GetProperty("close").GetString().Should().Be("14:00");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PolicySettings.AsNoTracking().ToListAsync()).Should().OnlyContain(s => s.UpdatedById == officerId);
    }

    [Fact]
    public async Task Keys_left_out_keep_their_values()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        var saved = ByKey(await (await officer.PutAsJsonAsync(Url, Body((PolicyKeys.MaxOpenRequests, "4")))).ReadJsonAsync());

        saved[PolicyKeys.MaxOpenRequests].GetProperty("value").GetString().Should().Be("4");
        saved[PolicyKeys.MinLeadTimeHours].GetProperty("value").GetString().Should().Be("48");
        saved[PolicyKeys.MinLeadTimeHours].GetProperty("updatedByName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    public static TheoryData<string, string, string> InvalidChanges => new()
    {
        { PolicyKeys.OpeningHours, PolicySettingDefaults.OpeningHoursJson.Replace("\"08:00\",\"close\":\"16:00\"", "\"8am\",\"close\":\"16:00\""), PolicyKeys.OpeningHours },
        { PolicyKeys.OpeningHours, PolicySettingDefaults.OpeningHoursJson.Replace("\"08:00\",\"close\":\"16:00\"", "\"16:00\",\"close\":\"08:00\""), PolicyKeys.OpeningHours },
        { PolicyKeys.OpeningHours, PolicySettingDefaults.OpeningHoursJson.Replace("\"08:00\",\"close\":\"16:00\"", "\"08:10\",\"close\":\"16:00\""), PolicyKeys.OpeningHours },
        { PolicyKeys.SlotGranularityMinutes, "20", PolicyKeys.SlotGranularityMinutes },
        { PolicyKeys.MaxDurationHours, "13", PolicyKeys.MaxDurationHours },
        { PolicyKeys.MinLeadTimeHours, "721", PolicyKeys.MinLeadTimeHours },
        { PolicyKeys.MaxAdvanceDaysStudent, "0", PolicyKeys.MaxAdvanceDaysStudent },
        { PolicyKeys.MaxCapacityRatio, "11", PolicyKeys.MaxCapacityRatio },
        { PolicyKeys.FreeCancellationHours, "-1", PolicyKeys.FreeCancellationHours },
        { PolicyKeys.MaxOpenRequests, "21", PolicyKeys.MaxOpenRequests },
        { PolicyKeys.MaxOpenRequests, "lots", PolicyKeys.MaxOpenRequests },
        { PolicyKeys.CheckoutWindowMinutes, "241", PolicyKeys.CheckoutWindowMinutes },
        { "max_group_size", "10", "max_group_size" },
    };

    [Theory]
    [MemberData(nameof(InvalidChanges))]
    public async Task Invalid_values_return_400_on_their_key_and_change_nothing(string key, string value, string field)
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        var response = await officer.PutAsJsonAsync(Url, Body((key, value), (PolicyKeys.MaxOpenRequests == key ? PolicyKeys.MinLeadTimeHours : PolicyKeys.MaxOpenRequests, "5")));

        var problem = await response.ShouldBeProblemAsync(400);
        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).Should().Equal(field);
        var list = ByKey(await (await officer.GetAsync(Url)).ReadJsonAsync());
        list.Values.Should().OnlyContain(s => s.GetProperty("updatedByName").ValueKind == JsonValueKind.Null);
        (await PolicyAuditsAsync(factory)).Should().BeEmpty();
    }

    [Fact]
    public async Task All_days_closed_returns_400_on_opening_hours()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        const string closed = """{"mon":null,"tue":null,"wed":null,"thu":null,"fri":null,"sat":null,"sun":null}""";

        var problem = await (await officer.PutAsJsonAsync(Url, Body((PolicyKeys.OpeningHours, closed)))).ShouldBeProblemAsync(400);

        problem.GetProperty("errors").GetProperty(PolicyKeys.OpeningHours)[0].GetString().Should().Contain("At least one day");
    }

    [Fact]
    public async Task Changing_granularity_to_60_while_a_day_opens_at_half_past_returns_400()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var halfPast = PolicySettingDefaults.OpeningHoursJson.Replace("\"08:00\",\"close\":\"16:00\"", "\"08:30\",\"close\":\"16:00\"");
        (await officer.PutAsJsonAsync(Url, Body((PolicyKeys.OpeningHours, halfPast)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await officer.PutAsJsonAsync(Url, Body((PolicyKeys.SlotGranularityMinutes, "60")));

        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty(PolicyKeys.OpeningHours)[0].GetString()
            .Should().Contain("sat").And.Contain("60");
    }

    [Fact]
    public async Task Several_bad_keys_are_all_reported()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        var problem = await (await officer.PutAsJsonAsync(Url, Body((PolicyKeys.MaxOpenRequests, "0"), (PolicyKeys.MaxCapacityRatio, "0"))))
            .ShouldBeProblemAsync(400);

        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name)
            .Should().BeEquivalentTo(PolicyKeys.MaxOpenRequests, PolicyKeys.MaxCapacityRatio);
    }

    [Fact]
    public async Task A_repeated_key_or_empty_body_returns_400()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        var repeated = await officer.PutAsJsonAsync(Url, Body((PolicyKeys.MaxOpenRequests, "4"), (PolicyKeys.MaxOpenRequests, "5")));
        var empty = await officer.PutAsJsonAsync(Url, new { settings = Array.Empty<object>() });

        (await repeated.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty(PolicyKeys.MaxOpenRequests, out _).Should().BeTrue();
        await empty.ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task ValueType_and_description_in_the_body_are_ignored()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        var response = await officer.PutAsJsonAsync(Url, new
        {
            settings = new[] { new { key = PolicyKeys.MaxOpenRequests, value = "4", valueType = "json", description = "hacked" } },
        });

        var saved = ByKey(await response.ReadJsonAsync())[PolicyKeys.MaxOpenRequests];
        saved.GetProperty("value").GetString().Should().Be("4");
        saved.GetProperty("valueType").GetString().Should().Be(PolicyValueTypes.Int);
        saved.GetProperty("description").GetString().Should().NotBe("hacked");
    }

    [Fact]
    public async Task Each_changed_key_writes_one_audit_row_with_old_and_new_values()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        // 3 -> 2: one row.
        (await officer.PutAsJsonAsync(Url, Body((PolicyKeys.MaxCapacityRatio, "2")))).StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await PolicyAuditsAsync(factory);
        first.Should().ContainSingle();
        first[0].Should().Match<AuditLog>(a => a.Action == AuditActions.Updated && a.EntityId == PolicyKeys.MaxCapacityRatio && a.UserId == officerId);
        var details = JsonDocument.Parse(first[0].DetailsJson).RootElement;
        details.GetProperty("key").GetString().Should().Be(PolicyKeys.MaxCapacityRatio);
        details.GetProperty("old").GetString().Should().Be("3");
        details.GetProperty("new").GetString().Should().Be("2");

        // Unchanged values (also spelled differently): no row.
        (await officer.PutAsJsonAsync(Url, Body((PolicyKeys.MaxCapacityRatio, "2.0"), (PolicyKeys.MinLeadTimeHours, "48"))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PolicyAuditsAsync(factory)).Should().HaveCount(1);

        // Two keys change: two rows. No names-only row from the generic IAuditable path.
        (await officer.PutAsJsonAsync(Url, Body((PolicyKeys.MaxCapacityRatio, "3"), (PolicyKeys.MaxOpenRequests, "4"))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var all = await PolicyAuditsAsync(factory);
        all.Should().HaveCount(3);
        all.Skip(1).Select(a => a.EntityId).Should().BeEquivalentTo(PolicyKeys.MaxCapacityRatio, PolicyKeys.MaxOpenRequests);
        all.Should().OnlyContain(a => !a.DetailsJson.Contains("changed"));
    }
}
