using System.Net;
using System.Text.Json;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class HealthEndpointTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Get_health_returns_200_and_healthy_database_check()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        var database = json.RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "database");
        database.GetProperty("status").GetString().Should().Be("Healthy");
    }
}
