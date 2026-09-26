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

[Collection(PostgresCollection.Name)]
public class ClubsEndpointsTests(PostgresFixture fixture)
{
    private async Task<HttpClient> AdminAsync() => (await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Admin)).Client;

    private async Task<long> UserAsync(string role) => (await TestAuth.CreateUserClientAsync(fixture.Factory, role)).UserId;

    private static async Task<long> CreateClubAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/clubs", new { name = $"Club {Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
    }

    private static Task<HttpResponseMessage> AddMemberAsync(HttpClient admin, long clubId, long userId, bool isRepresentative = false) =>
        admin.PostAsJsonAsync($"/api/clubs/{clubId}/members", new { userId, isRepresentative });

    private static long[] Representatives(JsonElement club) => club.GetProperty("members").EnumerateArray()
        .Where(m => m.GetProperty("isRepresentative").GetBoolean())
        .Select(m => m.GetProperty("userId").GetInt64()).ToArray();

    [Fact]
    public async Task Create_returns_201_with_location_and_a_duplicate_name_in_other_casing_returns_409()
    {
        var admin = await AdminAsync();
        var name = $"Chess {Guid.NewGuid():N}";

        var created = await admin.PostAsJsonAsync("/api/clubs", new { name = $"  {name} " });
        var duplicate = await admin.PostAsJsonAsync("/api/clubs", new { name = name.ToUpperInvariant() });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.ReadJsonAsync();
        body.GetProperty("name").GetString().Should().Be(name);
        created.Headers.Location!.AbsolutePath.Should().Be($"/api/clubs/{body.GetProperty("id").GetInt64()}");
        await duplicate.ShouldBeProblemAsync(409);
    }

    [Fact]
    public async Task Setting_a_new_representative_unsets_the_old_one()
    {
        var admin = await AdminAsync();
        var clubId = await CreateClubAsync(admin);
        var (first, second) = (await UserAsync(Roles.Student), await UserAsync(Roles.Lecturer));
        (await AddMemberAsync(admin, clubId, first, isRepresentative: true)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await AddMemberAsync(admin, clubId, second)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await admin.PutAsJsonAsync($"/api/clubs/{clubId}/representative", new { userId = second });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var club = await response.ReadJsonAsync();
        Representatives(club).Should().Equal(second);
        club.GetProperty("members").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Adding_a_member_as_representative_replaces_the_current_one()
    {
        var admin = await AdminAsync();
        var clubId = await CreateClubAsync(admin);
        var (first, second) = (await UserAsync(Roles.Student), await UserAsync(Roles.Student));
        await AddMemberAsync(admin, clubId, first, isRepresentative: true);

        var response = await AddMemberAsync(admin, clubId, second, isRepresentative: true);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var club = await response.ReadJsonAsync();
        Representatives(club).Should().Equal(second);
        var list = await (await admin.GetAsync($"/api/clubs?search={club.GetProperty("name").GetString()}")).ReadJsonAsync();
        list.GetProperty("items")[0].GetProperty("memberCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Representative_change_is_audited_as_two_member_updates()
    {
        var (admin, adminId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Admin);
        var clubId = await CreateClubAsync(admin);
        var (first, second) = (await UserAsync(Roles.Student), await UserAsync(Roles.Student));
        await AddMemberAsync(admin, clubId, first, isRepresentative: true);
        await AddMemberAsync(admin, clubId, second);

        await admin.PutAsJsonAsync($"/api/clubs/{clubId}/representative", new { userId = second });

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updates = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(ClubMember) && a.Action == AuditActions.Updated && a.EntityId!.StartsWith($"{clubId}:"))
            .ToListAsync();
        updates.Select(a => a.EntityId).Should().BeEquivalentTo($"{clubId}:{first}", $"{clubId}:{second}");
        updates.Should().OnlyContain(a => a.UserId == adminId);
    }

    [Theory]
    [InlineData(Roles.LabTechnician)]
    [InlineData(Roles.FacilitiesOfficer)]
    [InlineData(Roles.Admin)]
    public async Task Only_students_and_lecturers_can_be_members(string role)
    {
        var admin = await AdminAsync();
        var clubId = await CreateClubAsync(admin);

        var response = await AddMemberAsync(admin, clubId, await UserAsync(role));

        var problem = await response.ShouldBeProblemAsync(400);
        problem.GetProperty("errors").GetProperty("UserId")[0].GetString()
            .Should().Be("Only students and lecturers can be club members.");
    }

    [Fact]
    public async Task Adding_the_same_member_twice_returns_409_and_unknown_user_returns_400()
    {
        var admin = await AdminAsync();
        var clubId = await CreateClubAsync(admin);
        var student = await UserAsync(Roles.Student);
        await AddMemberAsync(admin, clubId, student);

        await (await AddMemberAsync(admin, clubId, student)).ShouldBeProblemAsync(409);
        await (await AddMemberAsync(admin, clubId, long.MaxValue)).ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task Representative_must_already_be_a_member()
    {
        var admin = await AdminAsync();
        var clubId = await CreateClubAsync(admin);

        var response = await admin.PutAsJsonAsync($"/api/clubs/{clubId}/representative", new { userId = await UserAsync(Roles.Student) });

        await response.ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task Remove_member_returns_204_then_404()
    {
        var admin = await AdminAsync();
        var clubId = await CreateClubAsync(admin);
        var student = await UserAsync(Roles.Student);
        await AddMemberAsync(admin, clubId, student);

        (await admin.DeleteAsync($"/api/clubs/{clubId}/members/{student}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await (await admin.DeleteAsync($"/api/clubs/{clubId}/members/{student}")).ShouldBeProblemAsync(404);
    }

    [Fact]
    public async Task Student_gets_403_on_every_write()
    {
        var student = TestAuth.CreateClient(fixture.Factory, Roles.Student);

        await (await student.PostAsJsonAsync("/api/clubs", new { name = "Sneaky Club" })).ShouldBeProblemAsync(403);
        await (await student.PutAsJsonAsync("/api/clubs/1", new { name = "x", isActive = false })).ShouldBeProblemAsync(403);
        await (await student.PostAsJsonAsync("/api/clubs/1/members", new { userId = 1 })).ShouldBeProblemAsync(403);
        await (await student.DeleteAsync("/api/clubs/1/members/1")).ShouldBeProblemAsync(403);
        await (await student.PutAsJsonAsync("/api/clubs/1/representative", new { userId = 1 })).ShouldBeProblemAsync(403);
    }

    [Fact]
    public async Task Deactivated_club_is_hidden_from_students_but_visible_to_admins_with_includeInactive()
    {
        var admin = await AdminAsync();
        var name = $"Hidden {Guid.NewGuid():N}";
        var id = (await (await admin.PostAsJsonAsync("/api/clubs", new { name })).ReadJsonAsync()).GetProperty("id").GetInt64();
        (await admin.PutAsJsonAsync($"/api/clubs/{id}", new { name, isActive = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        var student = TestAuth.CreateClient(fixture.Factory, Roles.Student);

        await (await student.GetAsync($"/api/clubs/{id}")).ShouldBeProblemAsync(404);
        var studentList = await (await student.GetAsync($"/api/clubs?search={name}&includeInactive=true")).ReadJsonAsync();
        studentList.GetProperty("total").GetInt32().Should().Be(0);

        (await admin.GetAsync($"/api/clubs/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var adminList = await (await admin.GetAsync($"/api/clubs?search={name}&includeInactive=true")).ReadJsonAsync();
        adminList.GetProperty("total").GetInt32().Should().Be(1);
        adminList.GetProperty("items")[0].GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Any_signed_in_user_can_list_and_anonymous_gets_401()
    {
        (await TestAuth.CreateClient(fixture.Factory, Roles.LabTechnician).GetAsync("/api/clubs"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await (await fixture.Factory.CreateClient().GetAsync("/api/clubs")).ShouldBeProblemAsync(401);
    }
}
