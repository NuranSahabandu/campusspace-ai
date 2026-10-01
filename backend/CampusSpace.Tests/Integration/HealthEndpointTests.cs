using System.Net;
using System.Text.Json;
using CampusSpace.Api.Health;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class HealthEndpointTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Get_health_returns_200_and_healthy_database_and_agent_service_checks()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        CheckStatus(json, "database").Should().Be("Healthy");
        CheckStatus(json, "agent-service").Should().Be("Healthy");
    }

    [Fact]
    public async Task Get_health_returns_200_and_degraded_when_agent_service_is_unreachable()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(AgentServiceHealthCheck.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new StubHttpMessageHandler(_ => throw new HttpRequestException("Connection refused")))));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().Should().Be("Degraded");
        CheckStatus(json, "database").Should().Be("Healthy");
        CheckStatus(json, "agent-service").Should().Be("Degraded");
    }

    [Fact]
    public async Task Get_health_live_answers_200_without_calling_the_agent_service()
    {
        var agentCalls = 0;
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(AgentServiceHealthCheck.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ =>
                {
                    Interlocked.Increment(ref agentCalls);
                    throw new HttpRequestException("Connection refused");
                }))));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
        agentCalls.Should().Be(0);
    }

    [Fact]
    public async Task Get_health_live_answers_200_when_the_database_is_unreachable_while_health_reports_it()
    {
        // Nothing listens on port 1: any database call fails. The liveness route must not make one.
        await using var factory = new CustomWebApplicationFactory("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2");
        var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var health = await client.GetAsync("/health");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        health.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var json = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        CheckStatus(json, "database").Should().Be("Unhealthy");
    }

    [Theory]
    [InlineData("/health/live", Serilog.Events.LogEventLevel.Verbose)]
    [InlineData("/HEALTH/LIVE", Serilog.Events.LogEventLevel.Verbose)]
    [InlineData("/health", Serilog.Events.LogEventLevel.Information)]
    [InlineData("/api/rooms", Serilog.Events.LogEventLevel.Information)]
    public void Liveness_pings_are_logged_at_verbose_only(string path, Serilog.Events.LogEventLevel expected)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = 200;

        HealthLogLevels.ForRequest(context, 1, null).Should().Be(expected);
        context.Response.StatusCode = 500;
        HealthLogLevels.ForRequest(context, 1, null).Should().Be(Serilog.Events.LogEventLevel.Error);
    }

    private static string? CheckStatus(JsonDocument json, string name) =>
        json.RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == name)
            .GetProperty("status").GetString();
}
