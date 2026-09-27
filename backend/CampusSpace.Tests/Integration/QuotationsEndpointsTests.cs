using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.QuotationTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>POST /api/quotations/preview, GET /api/quotations/{id} and GET /api/booking-requests/{id}/quotation.</summary>
[Collection(PostgresCollection.Name)]
public class QuotationsEndpointsTests(PostgresFixture fixture)
{
    private const string PreviewUrl = "/api/quotations/preview";
    private static readonly DateOnly Tuesday = new(2026, 10, 6);

    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static object Body(long roomId, string? requesterRole = null, object[]? equipment = null, DateOnly? date = null) => new
    {
        roomId,
        start = CampusTime.At(date ?? Tuesday, new TimeOnly(14, 0)),
        end = CampusTime.At(date ?? Tuesday, new TimeOnly(17, 0)),
        equipment = equipment ?? [],
        requesterRole,
    };

    [Fact]
    public async Task Preview_prices_the_demo_quote_by_role()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        long a301, mic;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Seed.SeedAsync(db, "Test#Password1");
            a301 = await db.Rooms.Where(r => r.Code == "A301").Select(r => r.Id).SingleAsync();
            mic = await db.EquipmentTypes.Where(t => t.Code == "MIC-WIRELESS").Select(t => t.Id).SingleAsync();
        }
        object[] mics = [new { typeId = mic, quantity = 2 }];

        var student = await (await TestAuth.CreateClient(factory, Roles.Student)
            .PostAsJsonAsync(PreviewUrl, Body(a301, equipment: mics))).ReadJsonAsync();
        student.GetProperty("total").GetDecimal().Should().Be(5500.00m);
        student.GetProperty("discount").GetDecimal().Should().Be(0m);
        student.GetProperty("exempt").GetBoolean().Should().BeFalse();
        student.GetProperty("currency").GetString().Should().Be("LKR");
        student.GetProperty("id").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        var lines = student.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("description").GetString()).Should().Equal(
            "Computer lab A301, 3 h @ LKR 1,500", "Wireless microphone x2 @ LKR 500");
        lines[0].GetProperty("kind").GetString().Should().Be(QuotationLineKinds.Room);

        var lecturer = await (await TestAuth.CreateClient(factory, Roles.Lecturer)
            .PostAsJsonAsync(PreviewUrl, Body(a301, equipment: mics))).ReadJsonAsync();
        lecturer.GetProperty("subtotal").GetDecimal().Should().Be(1000m);
        lecturer.GetProperty("discount").GetDecimal().Should().Be(1000m);
        lecturer.GetProperty("discountReason").GetString().Should().Be(QuotationCalculator.LecturerExemptionReason);
        lecturer.GetProperty("exempt").GetBoolean().Should().BeTrue();
        lecturer.GetProperty("total").GetDecimal().Should().Be(0m);

        var officer = TestAuth.CreateClient(factory, Roles.FacilitiesOfficer);
        (await (await officer.PostAsJsonAsync(PreviewUrl, Body(a301, RequesterRoles.Student, mics))).ReadJsonAsync())
            .GetProperty("total").GetDecimal().Should().Be(5500.00m);

        var sunday = await officer.PostAsJsonAsync(PreviewUrl, Body(a301, RequesterRoles.Student, mics, new DateOnly(2026, 10, 11)));
        (await sunday.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty("Start")[0].GetString()
            .Should().Be("The campus is closed on Sundays");

        // Nothing was saved.
        await using var check = factory.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().Quotations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Preview_role_rules()
    {
        var student = await TestAuth.CreateClient(Factory, Roles.Student).PostAsJsonAsync(PreviewUrl, Body(1, RequesterRoles.Lecturer));
        (await student.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty("RequesterRole")[0].GetString()
            .Should().Be(QuotationService.OwnRoleMessage);

        var officer = await TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer).PostAsJsonAsync(PreviewUrl, Body(1));
        (await officer.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty("RequesterRole")[0].GetString()
            .Should().Be(QuotationService.RoleRequiredMessage);

        var badRole = await TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer).PostAsJsonAsync(PreviewUrl, Body(1, Roles.Admin));
        (await badRole.ShouldBeProblemAsync(400)).GetProperty("errors").TryGetProperty("RequesterRole", out _).Should().BeTrue();

        (await TestAuth.CreateClient(Factory, Roles.LabTechnician).PostAsJsonAsync(PreviewUrl, Body(1, RequesterRoles.Student)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestAuth.CreateClient(Factory, Roles.Admin).PostAsJsonAsync(PreviewUrl, Body(1, RequesterRoles.Student)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Factory.CreateClient().PostAsJsonAsync(PreviewUrl, Body(1, RequesterRoles.Student)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Preview_of_an_unknown_room_is_404()
    {
        var response = await TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer)
            .PostAsJsonAsync(PreviewUrl, Body(long.MaxValue, RequesterRoles.Student));

        await response.ShouldBeProblemAsync(404);
    }

    [Fact]
    public async Task Owner_and_officer_read_a_quote_other_requesters_cannot()
    {
        var (owner, _, requestId) = await RequestAsync(Factory);
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        await CreateDraftAsync(Factory, requestId, Quote());
        var id = await CreateDraftAsync(Factory, requestId, Quote(type.Id, 500m));
        var (other, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        var officer = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);
        string[] urls = [$"/api/quotations/{id}", $"/api/booking-requests/{requestId}/quotation"];

        foreach (var url in urls)
        {
            foreach (var client in new[] { owner, officer })
            {
                var body = await (await client.GetAsync(url)).ReadJsonAsync();
                body.GetProperty("id").GetInt64().Should().Be(id);
                body.GetProperty("requestId").GetInt64().Should().Be(requestId);
                body.GetProperty("status").GetString().Should().Be(QuotationStatuses.Draft);
                body.GetProperty("lines").GetArrayLength().Should().Be(2);
                body.GetProperty("total").GetDecimal().Should().Be(5500m);
                body.GetProperty("exempt").GetBoolean().Should().BeFalse();
            }
            await (await other.GetAsync(url)).ShouldBeProblemAsync(403);
            (await TestAuth.CreateClient(Factory, Roles.LabTechnician).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Unknown_quote_or_request_without_a_live_quote_is_404()
    {
        var (owner, _, requestId) = await RequestAsync(Factory);
        var officer = TestAuth.CreateClient(Factory, Roles.FacilitiesOfficer);

        (await officer.GetAsync($"/api/quotations/{long.MaxValue}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await officer.GetAsync($"/api/booking-requests/{long.MaxValue}/quotation")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/booking-requests/{requestId}/quotation")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
