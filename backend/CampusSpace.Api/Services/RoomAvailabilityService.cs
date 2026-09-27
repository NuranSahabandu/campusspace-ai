using CampusSpace.Api.Auth;
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
    ICurrentUser currentUser,
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

    public async Task<RoomScheduleDto?> GetScheduleAsync(long roomId, DateOnly date, CancellationToken ct = default)
    {
        var isOfficer = currentUser.IsInRole(Roles.FacilitiesOfficer);
        if (!await db.Rooms.AnyAsync(r => r.Id == roomId && (r.IsActive || isOfficer), ct))
            return null;

        var snapshot = await policy.GetAsync(ct);
        var day = CampusTime.UtcRange(CampusTime.StartOf(date), CampusTime.StartOf(date.AddDays(1)));
        // Bookings select only their times: the schedule never reveals who booked or why.
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => b.RoomId == roomId && BookingStatuses.Active.Contains(b.Status) && b.TimeRange.Overlaps(day))
            .Select(b => new BusyIntervalDto(b.TimeRange.LowerBound, b.TimeRange.UpperBound, ScheduleKinds.Booking, ScheduleKinds.BookedLabel))
            .ToListAsync(ct);
        var blackouts = await db.RoomBlackouts.AsNoTracking()
            .Where(b => b.RoomId == roomId && b.TimeRange.Overlaps(day))
            .Select(b => new BusyIntervalDto(b.TimeRange.LowerBound, b.TimeRange.UpperBound, ScheduleKinds.Blackout, b.Reason))
            .ToListAsync(ct);

        return RoomSchedule.Build(date, snapshot.OpeningHours[date.DayOfWeek], snapshot.SlotGranularityMinutes, bookings.Concat(blackouts));
    }
}
