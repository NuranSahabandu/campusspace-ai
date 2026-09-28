using System.Net;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// Swagger is served only in Development, anonymously, and without the internal agent-tool routes. Runs the real
/// Development pipeline (auto-migrate and seed) on its own database.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SwaggerDevelopmentTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Swagger_json_is_anonymous_and_hides_internal_routes()
    {
        await using var isolated = await fixture.CreateIsolatedFactoryAsync();
        await using var factory = isolated.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

        var response = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = (await response.ReadJsonAsync()).GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.Should().Contain("/api/auth/login");
        paths.Should().NotContain(p => p.StartsWith("/internal", StringComparison.OrdinalIgnoreCase));
    }
}
