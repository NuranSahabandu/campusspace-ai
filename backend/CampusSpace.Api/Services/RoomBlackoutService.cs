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

    public async Task<BlackoutDto?> CreateAsync(long roomId, CreateBlackoutRequest request, CancellationToken ct = default)
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
        return await GetAsync(roomId, blackout.Id, ct);
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

    private static IQueryable<BlackoutDto> ToDtos(IQueryable<RoomBlackout> blackouts) => blackouts.Select(b => new BlackoutDto(
        b.Id, b.RoomId, b.TimeRange.LowerBound, b.TimeRange.UpperBound, b.Reason, b.CreatedById, b.CreatedBy.FullName, b.CreatedAt));
}
