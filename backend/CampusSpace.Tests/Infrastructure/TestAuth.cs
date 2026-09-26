using System.Net.Http.Headers;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Options;
using CampusSpace.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>Mints tokens with the real TokenService and the factory's Jwt settings.</summary>
public static class TestAuth
{
    public static string Token(
        CustomWebApplicationFactory factory,
        string role,
        long userId = 1,
        TimeProvider? clock = null,
        string? keyOverride = null)
    {
        var jwt = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var options = new JwtOptions
        {
            Key = keyOverride ?? jwt.Key,
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            AccessTokenMinutes = jwt.AccessTokenMinutes,
        };
        var user = new User
        {
            Id = userId,
            FullName = $"Test {role}",
            Email = $"{role.ToLowerInvariant()}@campus.test",
            Role = role,
        };
        return new TokenService(Microsoft.Extensions.Options.Options.Create(options), clock ?? TimeProvider.System)
            .CreateAccessToken(user).Token;
    }

    public static HttpClient CreateClient(CustomWebApplicationFactory factory, string role, long userId = 1)
        => WithToken(factory.CreateClient(), Token(factory, role, userId));

    /// <summary>
    /// Inserts a real, active user with <paramref name="role"/> and returns a client signed in as them.
    /// Use it for writes: audited changes store the caller's id, which is an FK to Users.
    /// </summary>
    public static async Task<(HttpClient Client, long UserId)> CreateUserClientAsync(CustomWebApplicationFactory factory, string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            FullName = $"Test {role} {Guid.NewGuid():N}"[..40],
            Email = $"{Guid.NewGuid():N}@campus.test",
            PasswordHash = "not-a-real-hash",
            Role = role,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (CreateClient(factory, role, user.Id), user.Id);
    }

    public static HttpClient WithToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
