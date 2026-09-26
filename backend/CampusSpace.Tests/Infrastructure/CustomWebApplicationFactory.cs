using System.Security.Cryptography;
using CampusSpace.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Runs the real API in memory against the test container. Uses the "Testing" environment,
/// so user-secrets, Swagger and the Development auto-migration are all off.
/// </summary>
public sealed class CustomWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string JwtIssuer = "campusspace-api-tests";
    public const string JwtAudience = "campusspace-clients-tests";

    /// <summary>A fresh signing key for every test run (64 hex chars = 64 bytes).</summary>
    public string JwtKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
    }

    public async Task MigrateAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
}
