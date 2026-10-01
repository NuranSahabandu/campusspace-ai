using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class UsersEndpointsTests(PostgresFixture fixture)
{
    private async Task<string> RegisterAsync(string fullName)
    {
        var email = $"{Guid.NewGuid():N}@campus.test";
        var response = await fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { fullName, email, password = "correct-horse-1" });
        response.EnsureSuccessStatusCode();
        return email;
    }

    [Fact]
    public async Task Student_gets_403_problem_details()
    {
        var response = await TestAuth.CreateClient(fixture.Factory, Roles.Student).GetAsync("/api/users");

        await response.ShouldBeProblemAsync(403);
    }

    [Fact]
    public async Task Anonymous_gets_401_problem_details()
    {
        var response = await fixture.Factory.CreateClient().GetAsync("/api/users");

        await response.ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Admin_gets_200_with_the_paging_shape()
    {
        await RegisterAsync("Paging One");
        await RegisterAsync("Paging Two");

        var response = await TestAuth.CreateClient(fixture.Factory, Roles.Admin).GetAsync("/api/users?pageSize=1&page=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("pageSize").GetInt32().Should().Be(1);
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        var items = body.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].TryGetProperty("passwordHash", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Search_matches_name_or_email_case_insensitively_and_role_filters()
    {
        var marker = Guid.NewGuid().ToString("N")[..10];
        var email = await RegisterAsync($"Search {marker} Person");
        var admin = TestAuth.CreateClient(fixture.Factory, Roles.Admin);

        var byName = await (await admin.GetAsync($"/api/users?search={marker.ToUpperInvariant()}")).ReadJsonAsync();
        byName.GetProperty("total").GetInt32().Should().Be(1);
        byName.GetProperty("items")[0].GetProperty("email").GetString().Should().Be(email);

        var byEmail = await (await admin.GetAsync($"/api/users?search={email[..12]}")).ReadJsonAsync();
        byEmail.GetProperty("total").GetInt32().Should().Be(1);

        var wrongRole = await (await admin.GetAsync($"/api/users?search={marker}&role={Roles.Admin}")).ReadJsonAsync();
        wrongRole.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("pageSize=101", "PageSize")]
    [InlineData("page=0", "Page")]
    [InlineData("role=Janitor", "Role")]
    [InlineData("sort=password", "Sort")]
    public async Task Invalid_query_returns_400_with_field_errors(string query, string field)
    {
        var response = await TestAuth.CreateClient(fixture.Factory, Roles.Admin).GetAsync($"/api/users?{query}");

        var problem = await response.ShouldBeProblemAsync(400);
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    private static object NewUserBody(string role = Roles.Lecturer, string? email = null) => new
    {
        fullName = "  Dr. New Person ",
        email = email ?? $"{Guid.NewGuid():N}@campus.test",
        password = "initial-pass-1",
        role,
    };

    [Fact]
    public async Task Admin_creates_a_user_with_any_role_returns_201_with_location_and_can_get_it()
    {
        var admin = (await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Admin)).Client;
        var email = $"{Guid.NewGuid():N}@campus.test";

        var response = await admin.PostAsJsonAsync("/api/users", NewUserBody(Roles.FacilitiesOfficer, email.ToUpperInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.ReadJsonAsync();
        created.GetProperty("email").GetString().Should().Be(email);
        created.GetProperty("fullName").GetString().Should().Be("Dr. New Person");
        created.GetProperty("role").GetString().Should().Be(Roles.FacilitiesOfficer);
        created.TryGetProperty("passwordHash", out _).Should().BeFalse();
        var id = created.GetProperty("id").GetInt64();
        response.Headers.Location!.AbsolutePath.Should().Be($"/api/users/{id}");

        var fetched = await (await admin.GetAsync($"/api/users/{id}")).ReadJsonAsync();
        fetched.GetProperty("email").GetString().Should().Be(email);

        var login = await fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "initial-pass-1" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Creating_a_user_with_a_taken_email_returns_409()
    {
        var admin = (await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Admin)).Client;
        var email = await RegisterAsync("Existing Person");

        var response = await admin.PostAsJsonAsync("/api/users", NewUserBody(email: email.ToUpperInvariant()));

        var problem = await response.ShouldBeProblemAsync(409);
        problem.GetProperty("title").GetString().Should().Be("Email is already registered");
    }

    [Fact]
    public async Task Create_validates_role_and_password_length()
    {
        var admin = TestAuth.CreateClient(fixture.Factory, Roles.Admin);

        var response = await admin.PostAsJsonAsync("/api/users",
            new { fullName = "X", email = $"{Guid.NewGuid():N}@campus.test", password = "short", role = "Janitor" });

        var errors = (await response.ShouldBeProblemAsync(400)).GetProperty("errors");
        errors.TryGetProperty("Role", out _).Should().BeTrue();
        errors.TryGetProperty("Password", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Admin_updates_name_role_and_active_flag()
    {
        var admin = (await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Admin)).Client;
        var id = (await (await admin.PostAsJsonAsync("/api/users", NewUserBody(Roles.Student))).ReadJsonAsync())
            .GetProperty("id").GetInt64();

        var response = await admin.PutAsJsonAsync($"/api/users/{id}",
            new { fullName = "Renamed", role = Roles.LabTechnician, isActive = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        body.GetProperty("fullName").GetString().Should().Be("Renamed");
        body.GetProperty("role").GetString().Should().Be(Roles.LabTechnician);
        body.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves()
    {
        var (admin, adminId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Admin);
        var me = await (await admin.GetAsync($"/api/users/{adminId}")).ReadJsonAsync();

        var response = await admin.PutAsJsonAsync($"/api/users/{adminId}",
            new { fullName = me.GetProperty("fullName").GetString(), role = Roles.Admin, isActive = false });

        var problem = await response.ShouldBeProblemAsync(400);
        problem.GetProperty("title").GetString().Should().Be("You cannot deactivate your own account.");
        problem.GetProperty("errors").TryGetProperty("IsActive", out _).Should().BeTrue();
        (await (await admin.GetAsync($"/api/users/{adminId}")).ReadJsonAsync()).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Admin_cannot_change_their_own_role_but_can_rename_themselves()
    {
        var (admin, adminId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Admin);

        var demote = await admin.PutAsJsonAsync($"/api/users/{adminId}",
            new { fullName = "Me", role = Roles.Student, isActive = true });
        var rename = await admin.PutAsJsonAsync($"/api/users/{adminId}",
            new { fullName = "Me Renamed", role = Roles.Admin, isActive = true });

        var problem = await demote.ShouldBeProblemAsync(400);
        problem.GetProperty("title").GetString().Should().Be("You cannot change your own role.");
        rename.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_user_returns_404_and_student_gets_403_on_create()
    {
        var admin = TestAuth.CreateClient(fixture.Factory, Roles.Admin);
        var student = TestAuth.CreateClient(fixture.Factory, Roles.Student);

        await (await admin.GetAsync($"/api/users/{long.MaxValue}")).ShouldBeProblemAsync(404);
        await (await student.PostAsJsonAsync("/api/users", NewUserBody())).ShouldBeProblemAsync(403);
    }
}
