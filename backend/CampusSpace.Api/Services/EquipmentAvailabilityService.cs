using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Equipment;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace CampusSpace.Api.Services;

public sealed class EquipmentAvailabilityService(
    AppDbContext db,
    IPolicySettingsService policy,
    IBookingWindowRules windowRules) : IEquipmentAvailabilityService
{
    public async Task<EquipmentAvailabilityDto?> GetAvailabilityAsync(
        long typeId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var errors = windowRules.CheckSlot(start, end, await policy.GetAsync(ct))
            .ToFieldErrors(nameof(EquipmentAvailabilityQuery.Start), nameof(EquipmentAvailabilityQuery.End));
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);

        var counts = await CountsFor(db.EquipmentTypes.Where(t => t.Id == typeId), CampusTime.UtcRange(start, end))
            .SingleOrDefaultAsync(ct);
        return counts?.ToDto();
    }

    /// <summary>One row per type, counted in a single SQL query (correlated subqueries, no rows loaded).</summary>
    private IQueryable<TypeCounts> CountsFor(IQueryable<EquipmentType> types, NpgsqlRange<DateTime> window) =>
        types.AsNoTracking().Select(t => new TypeCounts(
            t.Id, t.Code, t.Name,
            t.Items.Count(i => i.Status == EquipmentItemStatuses.Available || i.Status == EquipmentItemStatuses.OnLoan),
            db.EquipmentReservations
                .Where(r => r.TypeId == t.Id
                    && BookingStatuses.Active.Contains(r.Booking.Status)
                    && r.TimeRange.Overlaps(window))
                .Sum(r => (int?)r.Quantity) ?? 0));

    private sealed record TypeCounts(long TypeId, string Code, string Name, int Serviceable, int Reserved)
    {
        public int Available => Math.Max(0, Serviceable - Reserved);

        public EquipmentAvailabilityDto ToDto() =>
            new(TypeId, Code, Name, Serviceable, Reserved, Available, OverAllocated: Reserved > Serviceable);
    }
}
