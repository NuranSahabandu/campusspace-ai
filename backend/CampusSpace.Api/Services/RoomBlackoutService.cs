using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace CampusSpace.Api.Services;

public sealed class RoomBlackoutService(AppDbContext db, ICurrentUser currentUser) : IRoomBlackoutService
{
    public async Task<PagedResult<BlackoutDto>?> ListAsync(long roomId, BlackoutsQuery query, CancellationToken ct = default)
    {
        if (!await db.Rooms.AnyAsync(r => r.Id == roomId, ct))
            return null;

        var blackouts = db.RoomBlackouts.AsNoTracking().Where(b => b.RoomId == roomId);
        if (query.From is not null || query.To is not null)
        {
            // [From, To) with a missing bound treated as infinite.
            var window = new NpgsqlRange<DateTime>(
                query.From?.UtcDateTime ?? default, true, query.From is null,
                query.To?.UtcDateTime ?? default, false, query.To is null);
            blackouts = blackouts.Where(b => b.TimeRange.Overlaps(window));
        }

        blackouts = query.Sort == "-start"
            ? blackouts.OrderByDescending(b => b.TimeRange.LowerBound).ThenBy(b => b.Id)
            : blackouts.OrderBy(b => b.TimeRange.LowerBound).ThenBy(b => b.Id);

        return await ToDtos(blackouts).ToPagedResultAsync(query, ct);
    }

    public Task<BlackoutDto?> GetAsync(long roomId, long blackoutId, CancellationToken ct = default) =>
        ToDtos(db.RoomBlackouts.AsNoTracking().Where(b => b.Id == blackoutId && b.RoomId == roomId)).SingleOrDefaultAsync(ct);

    public async Task<BlackoutWithClashesDto?> CreateAsync(long roomId, CreateBlackoutRequest request, CancellationToken ct = default)
    {
        if (!await db.Rooms.AnyAsync(r => r.Id == roomId, ct))
            return null;

        var blackout = new RoomBlackout
        {
            RoomId = roomId,
            TimeRange = CampusTime.UtcRange(request.Start!.Value, request.End!.Value),
            Reason = request.Reason.Trim(),
            CreatedById = currentUser.UserId
                ?? throw new InvalidOperationException("Creating a blackout needs an authenticated user."),
        };
        db.RoomBlackouts.Add(blackout);
        await db.SaveChangesAsync(ct);

        var dto = (await GetAsync(roomId, blackout.Id, ct))!;
        var clashes = await ClashesOf(roomId, blackout.TimeRange).ToListAsync(ct);
        return new BlackoutWithClashesDto(
            dto.Id, dto.RoomId, dto.Start, dto.End, dto.Reason, dto.CreatedById, dto.CreatedByName, dto.CreatedAt,
            clashes.Count, clashes);
    }

    public async Task<IReadOnlyList<BlackoutClashDto>?> GetClashesAsync(long roomId, long blackoutId, CancellationToken ct = default)
    {
        var range = await db.RoomBlackouts.Where(b => b.Id == blackoutId && b.RoomId == roomId)
            .Select(b => (NpgsqlRange<DateTime>?)b.TimeRange).SingleOrDefaultAsync(ct);
        if (range is null)
            return null;

        return await ClashesOf(roomId, range.Value).ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(long roomId, long blackoutId, CancellationToken ct = default)
    {
        var blackout = await db.RoomBlackouts.SingleOrDefaultAsync(b => b.Id == blackoutId && b.RoomId == roomId, ct);
        if (blackout is null)
            return false;

        db.RoomBlackouts.Remove(blackout);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Active bookings of the room overlapping <paramref name="range"/> (&& in SQL). They are reported, never cancelled.
    /// ToDtos counts ClashCount with the same predicate; keep the two in step.
    /// </summary>
    private IQueryable<BlackoutClashDto> ClashesOf(long roomId, NpgsqlRange<DateTime> range) => db.Bookings.AsNoTracking()
        .Where(b => b.RoomId == roomId && BookingStatuses.Active.Contains(b.Status) && b.TimeRange.Overlaps(range))
        .OrderBy(b => b.TimeRange.LowerBound).ThenBy(b => b.Id)
        .Select(b => new BlackoutClashDto(
            b.Id, b.RequestId, b.TimeRange.LowerBound, b.TimeRange.UpperBound, b.Status,
            b.Request.Requester.FullName, b.Request.Requester.Email));

    /// <summary>ClashCount is a correlated COUNT in the same SQL query, with ClashesOf's predicate (no N+1).</summary>
    private IQueryable<BlackoutDto> ToDtos(IQueryable<RoomBlackout> blackouts) => blackouts.Select(b => new BlackoutDto(
        b.Id, b.RoomId, b.TimeRange.LowerBound, b.TimeRange.UpperBound, b.Reason, b.CreatedById, b.CreatedBy.FullName, b.CreatedAt,
        db.Bookings.Count(bk => bk.RoomId == b.RoomId && BookingStatuses.Active.Contains(bk.Status) && bk.TimeRange.Overlaps(b.TimeRange))));
}
