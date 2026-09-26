using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class AuthEndpointsTests(PostgresFixture fixture)
{
    private const string Password = "correct-horse-1";

    // The container is shared across tests, so every account gets a unique email.
    private static string UniqueEmail() => $"{Guid.NewGuid():N}@campus.test";

    private HttpClient Client() => fixture.Factory.CreateClient();

    private async Task<(JsonElement Body, HttpResponseMessage Response)> RegisterAsync(string email, string password = Password)
    {
        var response = await Client().PostAsJsonAsync("/api/auth/register",
            new { fullName = "Kavindi Perera", email, password });
        return (await response.ReadJsonAsync(), response);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Client().PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<User> LoadUserAsync(string email)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
    }

    [Fact]
    public async Task Register_returns_201_with_location_token_and_stores_a_bcrypt_hash()
    {
        var email = UniqueEmail();

        var (body, response) = await RegisterAsync($"  {email.ToUpperInvariant()} ");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.AbsolutePath.Should()
            .Be($"/api/users/{body.GetProperty("user").GetProperty("id").GetInt64()}");
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("expiresAt").GetDateTime().Should().BeAfter(DateTime.UtcNow);
        var user = body.GetProperty("user");
        user.GetProperty("email").GetString().Should().Be(email);
        user.GetProperty("role").GetString().Should().Be(Roles.Student);
        user.TryGetProperty("passwordHash", out _).Should().BeFalse();

        var stored = await LoadUserAsync(email);
        stored.PasswordHash.Should().StartWith("$2").And.NotBe(Password);
        BCrypt.Net.BCrypt.Verify(Password, stored.PasswordHash).Should().BeTrue();
        stored.Role.Should().Be(Roles.Student);
    }

    [Fact]
    public async Task Register_ignores_a_role_in_the_body()
    {
        var email = UniqueEmail();

        var response = await Client().PostAsJsonAsync("/api/auth/register",
            new { fullName = "Sneaky", email, password = Password, role = Roles.Admin });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await LoadUserAsync(email)).Role.Should().Be(Roles.Student);
    }

    [Fact]
    public async Task Register_with_an_existing_email_in_different_casing_returns_409()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var (_, response) = await RegisterAsync(email.ToUpperInvariant());

        var problem = await response.ShouldBeProblemAsync(409);
        problem.GetProperty("title").GetString().Should().Be("Email is already registered");
    }

    [Fact]
    public async Task Register_with_an_invalid_body_returns_400_with_field_errors()
    {
        var response = await Client().PostAsJsonAsync("/api/auth/register",
            new { fullName = "", email = "not-an-email", password = "7chars!" });

        var problem = await response.ShouldBeProblemAsync(400);
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("FullName", out _).Should().BeTrue();
        errors.TryGetProperty("Email", out _).Should().BeTrue();
        errors.TryGetProperty("Password", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Login_returns_a_token_whose_role_is_Student_and_sub_is_the_user_id()
    {
        var email = UniqueEmail();
        var (registered, _) = await RegisterAsync(email);

        var response = await LoginAsync(email.ToUpperInvariant(), Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        var jwt = new JsonWebToken(body.GetProperty("accessToken").GetString());
        jwt.GetClaim(JwtClaimNames.Role).Value.Should().Be(Roles.Student);
        jwt.GetClaim(JwtClaimNames.Sub).Value.Should()
            .Be(registered.GetProperty("user").GetProperty("id").GetInt64().ToString());
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_return_the_same_401()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var wrongPassword = await (await LoginAsync(email, "wrong-password")).ShouldBeProblemAsync(401);
        var unknownEmail = await (await LoginAsync(UniqueEmail(), Password)).ShouldBeProblemAsync(401);

        // Identical apart from traceId, which is unique per request by design.
        static string WithoutTraceId(JsonElement body) => JsonSerializer.Serialize(
            body.EnumerateObject().Where(p => p.Name != "traceId").ToDictionary(p => p.Name, p => p.Value));
        WithoutTraceId(unknownEmail).Should().Be(WithoutTraceId(wrongPassword));
        wrongPassword.GetProperty("title").GetString().Should().Be("Invalid email or password");
    }

    [Fact]
    public async Task Inactive_user_gets_the_same_401_on_login_and_on_me()
    {
        var email = UniqueEmail();
        var (registered, _) = await RegisterAsync(email);
        var token = registered.GetProperty("accessToken").GetString()!;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Email == email).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        var login = await (await LoginAsync(email, Password)).ShouldBeProblemAsync(401);
        login.GetProperty("title").GetString().Should().Be("Invalid email or password");
        var me = await TestAuth.WithToken(Client(), token).GetAsync("/api/auth/me");
        await me.ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Me_without_a_token_returns_401_problem_details()
    {
        var response = await Client().GetAsync("/api/auth/me");

        await response.ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Me_with_a_login_token_returns_that_user()
    {
        var email = UniqueEmail();
        var (registered, _) = await RegisterAsync(email);
        var login = await (await LoginAsync(email, Password)).ReadJsonAsync();

        var response = await TestAuth.WithToken(Client(), login.GetProperty("accessToken").GetString()!)
            .GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.ReadJsonAsync();
        me.GetProperty("id").GetInt64().Should().Be(registered.GetProperty("user").GetProperty("id").GetInt64());
        me.GetProperty("email").GetString().Should().Be(email);
        me.GetProperty("role").GetString().Should().Be(Roles.Student);
    }

    [Fact]
    public async Task Token_signed_with_a_different_key_returns_401()
    {
        var token = TestAuth.Token(fixture.Factory, Roles.Admin, keyOverride: new string('x', 64));

        var response = await TestAuth.WithToken(Client(), token).GetAsync("/api/auth/me");

        await response.ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        // Issued 3h ago with a 120-minute lifetime: expired an hour ago, far beyond the 30s skew.
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(-3));
        var token = TestAuth.Token(fixture.Factory, Roles.Admin, clock: clock);

        var response = await TestAuth.WithToken(Client(), token).GetAsync("/api/auth/me");

        await response.ShouldBeProblemAsync(401);
    }
}
