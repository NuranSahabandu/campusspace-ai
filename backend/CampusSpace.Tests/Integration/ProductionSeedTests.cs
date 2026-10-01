using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Options;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The Production seed: reference and demo data only (no requests, bookings or blackouts), idempotent, and the demo
/// password read from the environment variable Seed__DemoPassword (never Git), with the Production refusals.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ProductionSeedTests(PostgresFixture fixture)
{
    private const string EnvVar = "Seed__DemoPassword";
    private const string ProductionPassword = "Prod-demo-password-4417";

    private static readonly Dictionary<string, string?> SeedOn = new() { ["Seed:OnStartup"] = "true" };

    /// <summary>Sets the environment variable for one start of the API (the collection runs one test at a time).</summary>
    private static async Task WithEnvAsync(string? value, Func<Task> act)
    {
        var before = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, value);
        try
        {
            await act();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, before);
        }
    }

    private static async Task<Dictionary<string, int>> CountsAsync(string connectionString)
    {
        await using var db = PostgresFixture.CreateDbContext(connectionString);
        return new Dictionary<string, int>
        {
            ["Users"] = await db.Users.CountAsync(),
            ["Clubs"] = await db.Clubs.CountAsync(),
            ["Features"] = await db.Features.CountAsync(),
            ["Buildings"] = await db.Buildings.CountAsync(),
            ["Rooms"] = await db.Rooms.CountAsync(),
            ["EquipmentTypes"] = await db.EquipmentTypes.CountAsync(),
            ["EquipmentItems"] = await db.EquipmentItems.CountAsync(),
            ["PricingRules"] = await db.PricingRules.CountAsync(),
            ["PolicySettings"] = await db.PolicySettings.CountAsync(),
            ["BookingRequests"] = await db.BookingRequests.CountAsync(),
            ["Bookings"] = await db.Bookings.CountAsync(),
            ["RoomBlackouts"] = await db.RoomBlackouts.CountAsync(),
            ["EquipmentLoans"] = await db.EquipmentLoans.CountAsync(),
            ["AgentRuns"] = await db.AgentRuns.CountAsync(),
        };
    }

    [Fact]
    public async Task Seeds_reference_data_only_idempotently_with_the_password_from_the_environment()
    {
        var database = await fixture.CreateDatabaseAsync();

        await WithEnvAsync(ProductionPassword, async () =>
        {
            await using (var api = new ProductionApi(database, SeedOn))
            {
                var login = await api.HttpsClient().PostAsJsonAsync("/api/auth/login",
                    new { email = "perera@campusspace.local", password = ProductionPassword });
                login.StatusCode.Should().Be(HttpStatusCode.OK);
                var devPassword = await api.HttpsClient().PostAsJsonAsync("/api/auth/login",
                    new { email = "perera@campusspace.local", password = SeedOptionsValidator.PublicDevelopmentPassword });
                devPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
            var first = await CountsAsync(database);

            await using (var again = new ProductionApi(database, SeedOn))
                (await again.Client().GetAsync("/health")).EnsureSuccessStatusCode();

            (await CountsAsync(database)).Should().Equal(first);
            first["Users"].Should().Be(Seed.DemoUsers.Count);
            first["Clubs"].Should().Be(Seed.DemoClubs.Count);
            first["Rooms"].Should().Be(Seed.DemoRooms.Count);
            first["EquipmentTypes"].Should().Be(Seed.DemoEquipmentTypes.Count);
            first["EquipmentItems"].Should().BeGreaterThan(0);
            first["PricingRules"].Should().Be(Seed.DemoPricingRules.Count);
            first["PolicySettings"].Should().Be(PolicySettingDefaults.All.Count);
            foreach (var none in new[] { "BookingRequests", "Bookings", "RoomBlackouts", "EquipmentLoans", "AgentRuns" })
                first[none].Should().Be(0, $"the Production seed adds no {none}");
        });
    }

    [Theory]
    [InlineData(null, SeedOptionsValidator.MissingMessage)]
    [InlineData("short-pw", SeedOptionsValidator.WeakMessage)]
    [InlineData(SeedOptionsValidator.PublicDevelopmentPassword, SeedOptionsValidator.PublicMessage)]
    public async Task Production_refuses_to_seed_without_a_strong_private_password(string? password, string message)
    {
        var database = await fixture.CreateDatabaseAsync();

        await WithEnvAsync(password, async () =>
        {
            await using var api = new ProductionApi(database, SeedOn);

            var act = () => api.Client();

            act.Should().Throw<Exception>().Which.ToString().Should().Contain(message);
            if (password is not null)
                act.Should().Throw<Exception>().Which.ToString().Should().NotContain(password);
        });
        (await CountsAsync(database))["Users"].Should().Be(0);
    }
}
