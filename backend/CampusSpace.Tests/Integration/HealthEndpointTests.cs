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

    private static string? CheckStatus(JsonDocument json, string name) =>
        json.RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == name)
            .GetProperty("status").GetString();
}
