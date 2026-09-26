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

    [Fact]
    public async Task Wildcards_in_search_match_literally()
    {
        var admin = TestAuth.CreateClient(fixture.Factory, Roles.Admin);

        var body = await (await admin.GetAsync("/api/users?search=%25")).ReadJsonAsync();

        body.GetProperty("total").GetInt32().Should().Be(0);
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
}
