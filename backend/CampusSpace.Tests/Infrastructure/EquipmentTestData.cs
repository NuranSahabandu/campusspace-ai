using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Equipment rows for endpoint tests. The shared test database is not seeded, so each test makes its own types and
/// items with unique codes, and inserts rows directly when it needs a state the API refuses to create (an item on loan).
/// </summary>
public static class EquipmentTestData
{
    /// <summary>Upper-case with a hyphen, matching the EquipmentTypes code format.</summary>
    public static string UniqueTypeCode() => "T-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    public static string UniqueAssetTag() => "EQ-T-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    public static async Task<EquipmentType> CreateTypeAsync(
        CustomWebApplicationFactory factory, string? code = null, string category = EquipmentCategories.Audio,
        decimal fee = 500m, string? coveredByFeatureCode = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = new EquipmentType
        {
            Code = code ?? UniqueTypeCode(), Name = "Test type", Category = category, FeePerBooking = fee,
            CoveredByFeatureCode = coveredByFeatureCode,
        };
        db.EquipmentTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    public static async Task<EquipmentItem> CreateItemAsync(
        CustomWebApplicationFactory factory, long typeId, string status = EquipmentItemStatuses.Available,
        string condition = EquipmentConditions.Good, string? assetTag = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new EquipmentItem { TypeId = typeId, AssetTag = assetTag ?? UniqueAssetTag(), Status = status, Condition = condition };
        db.EquipmentItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    /// <summary>
    /// Reserves <paramref name="quantity"/> of a type for an existing booking, copying the booking's range (as ReserveAsync
    /// does). Inserted directly, without the guard, so tests can build states such as over-allocation.
    /// </summary>
    public static async Task InsertReservationAsync(CustomWebApplicationFactory factory, long bookingId, long typeId, int quantity)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var range = await db.Bookings.Where(b => b.Id == bookingId).Select(b => b.TimeRange).SingleAsync();
        db.EquipmentReservations.Add(new EquipmentReservation { BookingId = bookingId, TypeId = typeId, Quantity = quantity, TimeRange = range });
        await db.SaveChangesAsync();
    }
}
