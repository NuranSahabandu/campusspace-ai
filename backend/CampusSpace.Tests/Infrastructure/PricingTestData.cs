using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Pricing rules for tests. Tests on the shared database use a date of their own (years away, random), so rules made
/// by different tests never share a (room type, role, date) and never affect each other's status.
/// </summary>
public static class PricingTestData
{
    public static DateOnly Today => CampusTime.Today(TimeProvider.System);

    /// <summary>A random date 5 to 2000 years from now.</summary>
    public static DateOnly UniqueFutureDate() => Today.AddDays(Random.Shared.Next(365 * 5, 365 * 2000));

    /// <summary>A random date 5 to 1000 years ago.</summary>
    public static DateOnly UniquePastDate() => Today.AddDays(-Random.Shared.Next(365 * 5, 365 * 1000));

    /// <summary>Inserts a rule directly, skipping the API's "not in the past" rule. Returns its id.</summary>
    public static async Task<long> InsertAsync(
        CustomWebApplicationFactory factory, string roomType, string role, DateOnly validFrom, decimal rate = 100m, bool exempt = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await InsertAsync(db, roomType, role, validFrom, rate, exempt);
    }

    public static async Task<long> InsertAsync(
        AppDbContext db, string roomType, string role, DateOnly validFrom, decimal rate = 100m, bool exempt = false)
    {
        var rule = new PricingRule { RoomType = roomType, RequesterRole = role, ValidFrom = validFrom, HourlyRate = rate, IsExempt = exempt };
        db.PricingRules.Add(rule);
        await db.SaveChangesAsync();
        return rule.Id;
    }
}
