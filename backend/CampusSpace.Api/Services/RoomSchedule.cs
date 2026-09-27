using System.Globalization;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Extensions;

namespace CampusSpace.Api.Services;

/// <summary>Builds a room's day schedule from its busy intervals. Pure, so the interval arithmetic is unit-tested.</summary>
public static class RoomSchedule
{
    /// <param name="date">The campus date.</param>
    /// <param name="hours">That day's opening hours from the policy; null when closed.</param>
    /// <param name="granularityMinutes">slot_granularity_minutes; boundaries count from campus midnight.</param>
    /// <param name="busy">Intervals overlapping the day, in any order; they may extend past it.</param>
    public static RoomScheduleDto Build(DateOnly date, DayHours? hours, int granularityMinutes, IEnumerable<BusyIntervalDto> busy)
    {
        var dayStart = CampusTime.StartOf(date).UtcDateTime;
        var dayEnd = CampusTime.StartOf(date.AddDays(1)).UtcDateTime;
        var clipped = busy
            .Select(b => b with { Start = Max(b.Start, dayStart), End = Min(b.End, dayEnd) })
            .Where(b => b.Start < b.End)
            .OrderBy(b => b.Start).ThenBy(b => b.End).ThenBy(b => b.Kind)
            .ToList();

        var free = new List<FreeIntervalDto>();
        if (hours is not null)
        {
            var granularity = TimeSpan.FromMinutes(granularityMinutes);
            var close = CampusTime.At(date, hours.Close).UtcDateTime;
            var cursor = CampusTime.At(date, hours.Open).UtcDateTime;

            void AddGap(DateTime from, DateTime to)
            {
                // Snap inward: a gap from 10:10 to 11:50 offers only the whole slots 10:30–11:30.
                var start = dayStart + Ceiling(from - dayStart, granularity);
                var end = dayStart + Floor(to - dayStart, granularity);
                if (start < end)
                    free.Add(new FreeIntervalDto(start, end));
            }

            foreach (var b in clipped)
            {
                if (cursor >= close)
                    break;
                if (b.Start > cursor)
                    AddGap(cursor, Min(b.Start, close));
                cursor = Max(cursor, b.End);
            }
            if (cursor < close)
                AddGap(cursor, close);
        }

        return new RoomScheduleDto(date, Format(hours?.Open), Format(hours?.Close), granularityMinutes, clipped, free);
    }

    private static TimeSpan Ceiling(TimeSpan value, TimeSpan unit) => TimeSpan.FromTicks((value.Ticks + unit.Ticks - 1) / unit.Ticks * unit.Ticks);

    private static TimeSpan Floor(TimeSpan value, TimeSpan unit) => TimeSpan.FromTicks(value.Ticks / unit.Ticks * unit.Ticks);

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string? Format(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);
}
