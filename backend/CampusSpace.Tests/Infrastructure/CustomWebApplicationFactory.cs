using System.Net;
using System.Security.Cryptography;
using CampusSpace.Api.Data;
using CampusSpace.Api.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Runs the real API in memory against the test container. Uses the "Testing" environment,
/// so user-secrets, Swagger and the Development auto-migration are all off.
/// The agent-service HttpClient is stubbed to answer 200. Pass <paramref name="clock"/> to freeze the API's TimeProvider
/// (for rules about "now", such as lead time).
/// </summary>
public sealed class CustomWebApplicationFactory(string connectionString, TimeProvider? clock = null) : WebApplicationFactory<Program>
{
    public const string JwtIssuer = "campusspace-api-tests";
    public const string JwtAudience = "campusspace-clients-tests";

    /// <summary>A fresh signing key for every test run (64 hex chars = 64 bytes).</summary>
    public string JwtKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>A fresh X-Agent-Key for every test run (AgentTools:Key), different from the JWT key.</summary>
    public string AgentToolsKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>This factory's own damage-photo folder (Storage:DamagePhotosPath), deleted on dispose.</summary>
    public string DamagePhotosPath { get; } =
        Path.Combine(Path.GetTempPath(), "campusspace-tests", "damage-photos", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
        builder.UseSetting("Storage:DamagePhotosPath", DamagePhotosPath);
        builder.UseSetting("AgentTools:Key", AgentToolsKey);
        // The agent service is not running in tests: its /health always answers 200.
        builder.ConfigureTestServices(services => services.AddHttpClient(AgentServiceHealthCheck.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        if (clock is not null)
            builder.ConfigureTestServices(services => services.AddSingleton(clock));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(DamagePhotosPath))
            Directory.Delete(DamagePhotosPath, recursive: true);
    }

    public async Task MigrateAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
}
