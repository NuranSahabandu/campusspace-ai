using System.Net;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Swagger;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// X-Agent-Key isolation (§7.1 rule 3, §15.3): only the agent key reaches /internal/agent-tools, and it reaches nothing
/// under /api. Both 401s have the same Problem Details shape.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AgentToolsAuthTests(PostgresFixture fixture)
{
    private const string PolicyUrl = AgentToolsAuth.Url + "/policy";

    private CustomWebApplicationFactory Factory => fixture.Factory;

    [Fact]
    public async Task Right_key_returns_200()
    {
        var response = await AgentToolsAuth.CreateClient(Factory).GetAsync(PolicyUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wrong-key-wrong-key-wrong-key-wrong-key")]
    public async Task Missing_blank_or_wrong_key_returns_401_problem_details(string? key)
    {
        var client = Factory.CreateClient();
        if (key is not null)
            AgentToolsAuth.WithAgentKey(client, key);

        await (await client.GetAsync(PolicyUrl)).ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Key_with_a_different_case_or_extra_characters_is_rejected()
    {
        foreach (var key in new[] { Factory.AgentToolsKey.ToLowerInvariant(), Factory.AgentToolsKey + "0", Factory.AgentToolsKey[1..] })
            (await AgentToolsAuth.WithAgentKey(Factory.CreateClient(), key).GetAsync(PolicyUrl))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.FacilitiesOfficer)]
    [InlineData(Roles.Admin)]
    public async Task User_jwt_of_any_role_is_rejected_on_internal_routes(string role)
    {
        await (await TestAuth.CreateClient(Factory, role).GetAsync(PolicyUrl)).ShouldBeProblemAsync(401);
    }

    [Theory]
    [InlineData("/api/rooms")]
    [InlineData("/api/booking-requests")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/policy-settings/public")]
    public async Task Agent_key_is_rejected_on_api_routes(string url)
    {
        await (await AgentToolsAuth.CreateClient(Factory).GetAsync(url)).ShouldBeProblemAsync(401);
    }

    [Fact]
    public async Task Internal_and_api_401s_have_the_same_shape()
    {
        var client = Factory.CreateClient();
        var internalProblem = await (await client.GetAsync(PolicyUrl)).ShouldBeProblemAsync(401);
        var apiProblem = await (await client.GetAsync("/api/auth/me")).ShouldBeProblemAsync(401);

        internalProblem.GetProperty("title").GetString().Should().Be(apiProblem.GetProperty("title").GetString());
        internalProblem.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(apiProblem.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Swagger_document_has_no_internal_paths()
    {
        var document = Factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        document.Paths.Keys.Should().NotBeEmpty().And.Contain("/api/rooms");
        document.Paths.Keys.Should().NotContain(path => path.StartsWith("/internal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Each_call_is_logged_with_route_and_status_but_never_the_key_or_query()
    {
        var capture = new CapturingLoggerProvider();
        await using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ILoggerFactory>(LoggerFactory.Create(logging => logging.AddProvider(capture)))));
        var key = Fixture(factory);

        await factory.CreateClient().GetAsync(PolicyUrl + "?secret=query-value-1");
        await AgentToolsAuth.WithAgentKey(factory.CreateClient(), key).GetAsync(PolicyUrl + "?secret=query-value-2");

        var lines = capture.Entries.Where(e => e.Message.StartsWith("Agent tool call")).Select(e => e.Message).ToList();
        lines.Should().HaveCount(2);
        lines[0].Should().Be("Agent tool call GET internal/agent-tools/policy returned 401 in " + lines[0].Split(" in ")[1]);
        lines[1].Should().Contain("GET internal/agent-tools/policy returned 200");
        lines.Should().NotContain(m => m.Contains("query-value") || m.Contains("secret="));
        // The key appears in no log line at all, from any category.
        capture.Entries.Select(e => e.Message).Should().NotContain(m => m.Contains(key));
    }

    private static string Fixture(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["AgentTools:Key"]!;
}
