using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class RoomAvailabilityService(
    AppDbContext db,
    IPolicySettingsService policy,
    IBookingWindowRules windowRules) : IRoomAvailabilityService
{
    public async Task<PagedResult<RoomDto>> FindAvailableAsync(AvailabilityCriteria criteria, PageQuery page, CancellationToken ct = default)
    {
        var errors = windowRules.CheckSlot(criteria.Start, criteria.End, await policy.GetAsync(ct))
            .ToFieldErrors(nameof(RoomAvailabilityQuery.Start), nameof(RoomAvailabilityQuery.End));
        var codes = FeatureCodeList.Normalize(criteria.Features);
        if (codes.Count > 0)
        {
            var known = await db.Features.Where(f => codes.Contains(f.Code)).Select(f => f.Code).ToListAsync(ct);
            var unknown = codes.Except(known).ToList();
            if (unknown.Count > 0)
                errors[nameof(RoomAvailabilityQuery.Features)] = [$"Unknown feature codes: {string.Join(", ", unknown)}."];
        }
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);

        var rooms = db.Rooms.AsNoTracking().Where(r => r.IsActive && r.Capacity >= criteria.MinCapacity);
        if (criteria.MaxCapacity is { } max)
            rooms = rooms.Where(r => r.Capacity <= max);
        if (criteria.BuildingId is { } buildingId)
            rooms = rooms.Where(r => r.BuildingId == buildingId);
        if (criteria.Type is { } type)
            rooms = rooms.Where(r => r.Type == type);
        if (codes.Count > 0)
            // Codes are distinct, so matching all of them means the count of matches equals the count of codes.
            rooms = rooms.Where(r => r.RoomFeatures.Count(rf => codes.Contains(rf.Feature.Code)) == codes.Count);
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var pattern = page.Search.ToContainsPattern();
            rooms = rooms.Where(r => EF.Functions.ILike(r.Code, pattern) || EF.Functions.ILike(r.Name, pattern));
        }

        // NOT EXISTS subqueries with the range && operator, served by the GiST indexes on (RoomId, TimeRange).
        var window = CampusTime.UtcRange(criteria.Start, criteria.End);
        rooms = rooms.Where(r =>
            !db.RoomBlackouts.Any(b => b.RoomId == r.Id && b.TimeRange.Overlaps(window))
            && !db.Bookings.Any(b => b.RoomId == r.Id && BookingStatuses.Active.Contains(b.Status) && b.TimeRange.Overlaps(window)));

        return await RoomService.ToDtos(rooms.OrderBy(r => r.Capacity).ThenBy(r => r.Code).ThenBy(r => r.Id))
            .ToPagedResultAsync(page, ct);
    }
}
