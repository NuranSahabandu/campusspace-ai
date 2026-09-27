using System.Data;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Equipment;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public async Task ReserveAsync(Booking booking, IReadOnlyList<ReservationLine> lines, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("ReserveAsync must run inside the approval transaction.");
        // Lock-then-recount is only safe under READ COMMITTED, where each statement sees what was committed before it
        // started. Under REPEATABLE READ or SERIALIZABLE the recount would read the transaction's snapshot, which can be
        // older than the lock, so two approvals could both see enough and over-allocate.
        if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException(
                "ReserveAsync needs a READ COMMITTED transaction: under a snapshot isolation level the count after the lock can miss reservations committed while waiting for it.");
        if (lines.FirstOrDefault(l => l.Quantity < 0) is { } negative)
            throw new ArgumentOutOfRangeException(nameof(lines), negative.Quantity, "Quantities can't be negative.");

        var wanted = lines.Where(l => l.Quantity > 0)
            .GroupBy(l => l.TypeId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (wanted.Count == 0)
            return;

        // Ascending order: two approvals that need some of the same types take their shared locks in the same order,
        // so neither can hold one lock while waiting for a lock the other holds (no deadlock).
        var typeIds = wanted.Keys.Order().ToList();
        foreach (var typeId in typeIds)
            await AdvisoryLocks.LockAsync(db.Database, AdvisoryLocks.EquipmentType, typeId, ct);

        // A new statement after the locks, so it sees every reservation committed by an approval that held them before.
        var counts = await CountsFor(db.EquipmentTypes.Where(t => typeIds.Contains(t.Id)), booking.TimeRange)
            .ToDictionaryAsync(c => c.TypeId, ct);
        var unknown = typeIds.Where(id => !counts.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            throw new BusinessRuleException("Equipment", $"Unknown equipment type ids: {string.Join(", ", unknown)}.");

        var shortfalls = typeIds.Select(id => counts[id]).Where(c => c.Available < wanted[c.TypeId])
            .Select(c => $"{c.Code} (requested {wanted[c.TypeId]}, available {c.Available})")
            .ToList();
        if (shortfalls.Count > 0)
            throw new ConflictException($"Not enough equipment: {string.Join("; ", shortfalls)}");

        // The navigation (not BookingId) so a booking added in this transaction but not yet saved works too.
        db.EquipmentReservations.AddRange(typeIds.Select(id => new EquipmentReservation
        {
            Booking = booking,
            TypeId = id,
            Quantity = wanted[id],
            TimeRange = booking.TimeRange,
        }));
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
