using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class AuditEndpointsTests(PostgresFixture fixture)
{
    private const string Password = "correct-horse-1";

    private async Task<(long Id, string Email)> RegisterAsync()
    {
        var email = $"{Guid.NewGuid():N}@campus.test";
        var response = await fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { fullName = "Audit Person", email, password = Password });
        response.EnsureSuccessStatusCode();
        return ((await response.ReadJsonAsync()).GetProperty("user").GetProperty("id").GetInt64(), email);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<List<AuditLog>> LogsAsync(Func<IQueryable<AuditLog>, IQueryable<AuditLog>> filter)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await filter(db.AuditLogs.AsNoTracking()).OrderBy(a => a.Id).ToListAsync();
    }

    [Fact]
    public async Task Successful_login_writes_a_Login_row_for_that_user()
    {
        var (id, email) = await RegisterAsync();

        (await LoginAsync(email, Password)).StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await LogsAsync(q => q.Where(a => a.Action == AuditActions.Login && a.EntityId == id.ToString()));
        logs.Should().ContainSingle().Which.UserId.Should().Be(id);
    }

    [Fact]
    public async Task Wrong_password_writes_LoginFailed_for_the_matched_user_without_the_password()
    {
        var (id, email) = await RegisterAsync();
        const string wrong = "wrong-password-xyz";

        await (await LoginAsync($"  {email.ToUpperInvariant()} ", wrong)).ShouldBeProblemAsync(401);

        var log = (await LogsAsync(q => q.Where(a => a.Action == AuditActions.LoginFailed && a.EntityId == id.ToString())))
            .Should().ContainSingle().Subject;
        log.UserId.Should().Be(id);
        log.EntityType.Should().Be(nameof(User));
        log.DetailsJson.Should().Be($$"""{"email": "{{email}}"}""");
        log.DetailsJson.Should().NotContain(wrong);
    }

    [Fact]
    public async Task Unknown_email_writes_LoginFailed_with_no_user_and_no_password()
    {
        var email = $"{Guid.NewGuid():N}@campus.test";
        const string password = "some-guess-123";

        await (await LoginAsync(email, password)).ShouldBeProblemAsync(401);

        // DetailsJson is jsonb (no LIKE), so match the email in memory.
        var log = (await LogsAsync(q => q.Where(a => a.Action == AuditActions.LoginFailed && a.EntityId == null)))
            .Where(a => a.DetailsJson.Contains(email))
            .Should().ContainSingle().Subject;
        log.UserId.Should().BeNull();
        log.EntityId.Should().BeNull();
        log.DetailsJson.Should().NotContain(password);
    }

    [Fact]
    public async Task Admin_lists_newest_first_filtered_by_action_and_user_with_the_user_name()
    {
        var (id, email) = await RegisterAsync();
        await LoginAsync(email, Password);
        await LoginAsync(email, Password);
        var admin = TestAuth.CreateClient(fixture.Factory, Roles.Admin);

        var response = await admin.GetAsync($"/api/audit-logs?action={AuditActions.Login}&userId={id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        body.GetProperty("total").GetInt32().Should().Be(2);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().OnlyContain(i => i.GetProperty("action").GetString() == AuditActions.Login
            && i.GetProperty("userName").GetString() == "Audit Person");
        items[0].GetProperty("id").GetInt64().Should().BeGreaterThan(items[1].GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Created_rows_are_filterable_by_entity_type_and_time_range()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var (id, _) = await RegisterAsync();
        var admin = TestAuth.CreateClient(fixture.Factory, Roles.Admin);

        var body = await (await admin.GetAsync(
            $"/api/audit-logs?entityType=User&action=Created&search={id}&from={Uri.EscapeDataString(before.ToString("O"))}&pageSize=100"))
            .ReadJsonAsync();

        body.GetProperty("items").EnumerateArray()
            .Should().Contain(i => i.GetProperty("entityId").GetString() == id.ToString()
                && i.GetProperty("details").GetProperty("changed").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Student_gets_403()
    {
        var response = await TestAuth.CreateClient(fixture.Factory, Roles.Student).GetAsync("/api/audit-logs");

        await response.ShouldBeProblemAsync(403);
    }

    [Theory]
    [InlineData("from=2026-09-02T00:00:00Z&to=2026-09-01T00:00:00Z", "From")]
    [InlineData("sort=action", "Sort")]
    public async Task Invalid_query_returns_400(string query, string field)
    {
        var response = await TestAuth.CreateClient(fixture.Factory, Roles.Admin).GetAsync($"/api/audit-logs?{query}");

        var problem = await response.ShouldBeProblemAsync(400);
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }
}
