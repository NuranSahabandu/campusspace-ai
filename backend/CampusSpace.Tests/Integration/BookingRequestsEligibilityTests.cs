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
public class BookingRequestsEligibilityTests(PostgresFixture fixture)
{
    private const string EligibilityUrl = $"{Url}/eligibility";

    private static async Task<JsonElement> EligibilityAsync(HttpClient client)
    {
        var response = await client.GetAsync(EligibilityUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ReadJsonAsync();
    }

    [Fact]
    public async Task Seeded_kavindi_can_submit_for_Robotics_Club_and_ishan_cannot()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        long kavindi, ishan;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Seed.SeedAsync(db, "demo-password-1");
            kavindi = await db.Users.Where(u => u.Email == "kavindi@campusspace.local").Select(u => u.Id).SingleAsync();
            ishan = await db.Users.Where(u => u.Email == "ishan@campusspace.local").Select(u => u.Id).SingleAsync();
        }

        var result = await EligibilityAsync(TestAuth.CreateClient(factory, Roles.Student, kavindi));

        result.GetProperty("canSubmit").GetBoolean().Should().BeTrue();
        result.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        result.GetProperty("clubs").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Equal("Robotics Club");
        result.GetProperty("openRequests").GetInt32().Should().Be(0);
        result.GetProperty("maxOpenRequests").GetInt32().Should().Be(3);
        result.GetProperty("clubRequired").GetBoolean().Should().BeTrue();

        // Ishan is a member of two clubs but represents neither.
        var denied = await EligibilityAsync(TestAuth.CreateClient(factory, Roles.Student, ishan));
        denied.GetProperty("canSubmit").GetBoolean().Should().BeFalse();
        denied.GetProperty("reason").GetString().Should().Be(BookingRequestService.NotRepresentativeMessage);
        denied.GetProperty("clubs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_student_who_represents_only_an_inactive_club_cannot_submit()
    {
        var (client, userId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Student);
        await CreateClubAsync(fixture.Factory, representativeId: userId, isActive: false);

        var result = await EligibilityAsync(client);

        result.GetProperty("canSubmit").GetBoolean().Should().BeFalse();
        result.GetProperty("reason").GetString().Should().Be(BookingRequestService.NotRepresentativeMessage);
        result.GetProperty("clubs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_lecturer_needs_no_club()
    {
        var (lecturer, _) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Lecturer);

        var result = await EligibilityAsync(lecturer);

        result.GetProperty("canSubmit").GetBoolean().Should().BeTrue();
        result.GetProperty("clubRequired").GetBoolean().Should().BeFalse();
        result.GetProperty("clubs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task At_the_cap_canSubmit_is_false_with_the_cap_message()
    {
        var (client, _, clubId) = await StudentRepAsync(fixture.Factory);
        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync(Url, Body(clubId, purpose: $"Meeting {i}"))).StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await EligibilityAsync(client);

        result.GetProperty("canSubmit").GetBoolean().Should().BeFalse();
        result.GetProperty("reason").GetString().Should().Be("You already have 3 open requests (the limit is 3)");
        result.GetProperty("openRequests").GetInt32().Should().Be(3);
        result.GetProperty("clubs").GetArrayLength().Should().Be(1);
    }
}
