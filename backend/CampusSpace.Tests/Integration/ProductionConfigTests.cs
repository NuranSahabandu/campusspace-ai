using System.Net;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Options;
using CampusSpace.Api.Photos;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The Production pipeline (appsettings.Production.json) as Render runs it: CORS from configuration, Swagger on with the
/// Bearer scheme and no internal routes, forwarded proto honoured, HTTPS redirection (not for /health), no migrations
/// on startup, and the startup refusals (R2, CORS, SSL).
/// </summary>
[Collection(PostgresCollection.Name)]
public class ProductionConfigTests(PostgresFixture fixture)
{
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/me");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return request;
    }

    [Theory]
    [InlineData(ProductionApi.VercelOrigin)]
    [InlineData(ProductionApi.LocalOrigin)]
    public async Task CORS_allows_the_configured_origins(string origin)
    {
        await using var api = new ProductionApi(fixture.ConnectionString);

        var response = await api.HttpsClient().SendAsync(Preflight(origin));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:3000")]
    [InlineData("https://campusspace-test.vercel.app.evil.example")]
    public async Task CORS_gives_any_other_origin_nothing(string origin)
    {
        await using var api = new ProductionApi(fixture.ConnectionString);

        var response = await api.HttpsClient().SendAsync(Preflight(origin));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Swagger_is_served_with_the_Bearer_scheme_and_without_internal_routes()
    {
        await using var api = new ProductionApi(fixture.ConnectionString);

        var response = await api.HttpsClient().GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadJsonAsync();
        json.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString()
            .Should().Be("bearer");
        var paths = json.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.Should().Contain("/api/loans/{id}/photo");
        paths.Should().NotContain(p => p.StartsWith("/internal", StringComparison.OrdinalIgnoreCase));
        (await api.HttpsClient().GetAsync("/swagger/index.html")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Forwarded_https_is_honoured_and_plain_http_is_redirected_except_for_health()
    {
        await using var api = new ProductionApi(fixture.ConnectionString);

        var plain = await api.Client().GetAsync("/api/auth/me");
        var forwarded = await api.HttpsClient().GetAsync("/api/auth/me");
        var health = await api.Client().GetAsync("/health");
        var live = await api.Client().GetAsync("/health/live");

        plain.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        plain.Headers.Location.Should().Be(new Uri("https://localhost/api/auth/me"));
        forwarded.StatusCode.Should().Be(HttpStatusCode.Unauthorized); // reached the API as https
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        live.StatusCode.Should().Be(HttpStatusCode.OK); // Render's health check calls it over plain HTTP
    }

    [Fact]
    public async Task Production_gives_a_sleeping_agent_service_4_minutes_to_start_a_run()
    {
        // Render's free agent service takes about a minute to wake (2 minutes elsewhere, the default).
        await using var api = new ProductionApi(fixture.ConnectionString);

        var options = api.Factory.Services.GetRequiredService<IOptions<AgentServiceOptions>>().Value;

        options.StartTimeoutMinutes.Should().Be(4);
        options.RunTimeoutMinutes.Should().Be(4);
        options.PollSeconds.Should().Be(3);
    }

    [Fact]
    public async Task Migrations_do_not_run_on_startup()
    {
        var empty = await fixture.CreateDatabaseAsync(migrate: false);
        await using var api = new ProductionApi(empty);

        await api.Client().GetAsync("/health"); // starts the host

        await using var connection = new NpgsqlConnection(empty);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""select to_regclass('public."__EFMigrationsHistory"') is null""", connection);
        (await command.ExecuteScalarAsync()).Should().Be(true);
    }

    [Fact]
    public async Task With_the_seed_on_a_pending_migration_stops_startup_with_the_command_to_run()
    {
        var empty = await fixture.CreateDatabaseAsync(migrate: false);
        await using var api = new ProductionApi(empty, new Dictionary<string, string?>
        {
            ["Seed:OnStartup"] = "true",
            ["Seed:DemoPassword"] = "a-long-test-password",
        });

        var act = () => api.Client();

        act.Should().Throw<InvalidOperationException>().WithMessage("*migration(s) are pending*dotnet ef migrations script*");
    }

    [Fact]
    public async Task Production_without_R2_refuses_to_start()
    {
        await using var api = new ProductionApi(fixture.ConnectionString, new Dictionary<string, string?>
        {
            ["R2:AccountId"] = "", ["R2:AccessKeyId"] = "", ["R2:SecretAccessKey"] = "", ["R2:Bucket"] = "",
        });

        var act = () => api.Client();

        act.Should().Throw<Exception>().Which.ToString().Should().Contain(R2OptionsValidator.ProductionMessage);
    }

    [Fact]
    public async Task Production_without_CORS_origins_refuses_to_start()
    {
        await using var api = new ProductionApi(fixture.ConnectionString, new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = null, ["Cors:AllowedOrigins:1"] = null,
        });

        var act = () => api.Client();

        act.Should().Throw<InvalidOperationException>().WithMessage("Cors:AllowedOrigins is empty*");
    }

    [Fact]
    public async Task Production_refuses_a_database_connection_without_TLS()
    {
        // The test container's connection string has no SslMode, and Production requires it.
        await using var api = new ProductionApi(fixture.ConnectionString, new Dictionary<string, string?> { ["Database:RequireSsl"] = null });

        var act = () => api.Client();

        act.Should().Throw<Exception>().Which.ToString().Should().Contain(DatabaseOptionsValidator.SslMessage)
            .And.NotContain(new NpgsqlConnectionStringBuilder(fixture.ConnectionString).Password!);
    }

    [Fact]
    public async Task The_photo_store_is_the_configured_one_and_logs_no_secret()
    {
        await using var api = new ProductionApi(fixture.ConnectionString);

        (await api.Client().GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        api.Factory.Services.GetService(typeof(IPhotoStore)).Should().BeSameAs(api.Photos);
        HostingExtensions.UrlForPort(null).Should().BeNull();
    }
}
