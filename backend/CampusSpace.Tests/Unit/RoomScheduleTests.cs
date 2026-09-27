using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Services;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>Free/busy arithmetic for one campus day (Monday 2031-03-10, open 08:00–20:00 unless stated).</summary>
public class RoomScheduleTests
{
    private static readonly DateOnly Monday = new(2031, 3, 10);
    private static readonly DayHours Weekday = new(new TimeOnly(8, 0), new TimeOnly(20, 0));

    private static DateTime At(string time, int dayOffset = 0) => CampusTime.At(Monday.AddDays(dayOffset), TimeOnly.Parse(time)).UtcDateTime;

    private static BusyIntervalDto Booking(string from, string to) => new(At(from), At(to), ScheduleKinds.Booking, ScheduleKinds.BookedLabel);

    private static BusyIntervalDto Blackout(DateTime from, DateTime to, string reason = "Maintenance") =>
        new(from, to, ScheduleKinds.Blackout, reason);

    private static FreeIntervalDto Free(string from, string to) => new(At(from), At(to));

    private static RoomScheduleDto Build(params BusyIntervalDto[] busy) => RoomSchedule.Build(Monday, Weekday, 30, busy);

    [Fact]
    public void An_empty_day_is_free_from_opening_to_closing()
    {
        var schedule = Build();

        schedule.Should().BeEquivalentTo(new
        {
            Date = Monday, Open = "08:00", Close = "20:00", GranularityMinutes = 30, Busy = Array.Empty<BusyIntervalDto>(),
        });
        schedule.Free.Should().Equal(Free("08:00", "20:00"));
    }

    [Fact]
    public void Free_time_is_the_gaps_between_busy_intervals_in_start_order()
    {
        var schedule = Build(Booking("14:00", "15:00"), Booking("10:00", "12:00"));

        schedule.Busy.Should().Equal(Booking("10:00", "12:00"), Booking("14:00", "15:00"));
        schedule.Free.Should().Equal(Free("08:00", "10:00"), Free("12:00", "14:00"), Free("15:00", "20:00"));
    }

    [Fact]
    public void Gaps_are_snapped_inward_to_the_granularity()
    {
        var schedule = Build(Blackout(At("10:10"), At("11:50")), Booking("12:00", "13:00"));

        // 11:50–12:00 is shorter than a slot, so it disappears.
        schedule.Free.Should().Equal(Free("08:00", "10:00"), Free("13:00", "20:00"));
        RoomSchedule.Build(Monday, Weekday, 60, [Booking("08:00", "10:30")]).Free.Should().Equal(Free("11:00", "20:00"));
    }

    [Fact]
    public void Overlapping_busy_intervals_are_all_listed_but_merged_for_free_time()
    {
        var schedule = Build(Booking("09:00", "11:00"), Blackout(At("10:00"), At("12:00")));

        schedule.Busy.Should().HaveCount(2);
        schedule.Free.Should().Equal(Free("08:00", "09:00"), Free("12:00", "20:00"));
    }

    [Fact]
    public void Intervals_are_clipped_to_the_campus_day()
    {
        var overnight = Blackout(At("18:00", dayOffset: -1), At("09:00"), "Carpet");
        var lateNight = Blackout(At("19:00"), At("02:00", dayOffset: 1), "Cleaning");

        var schedule = Build(overnight, lateNight);

        schedule.Busy.Should().Equal(
            Blackout(At("00:00"), At("09:00"), "Carpet"),
            Blackout(At("19:00"), At("00:00", dayOffset: 1), "Cleaning"));
        schedule.Free.Should().Equal(Free("09:00", "19:00"));
    }

    [Fact]
    public void Busy_time_outside_opening_hours_does_not_create_free_time()
    {
        Build(Booking("06:00", "07:00"), Booking("21:00", "22:00")).Free.Should().Equal(Free("08:00", "20:00"));
    }

    [Fact]
    public void A_fully_busy_day_has_no_free_time()
    {
        Build(Blackout(At("07:00"), At("21:00"))).Free.Should().BeEmpty();
    }

    [Fact]
    public void A_closed_day_has_no_hours_and_no_free_time_but_still_lists_busy_time()
    {
        var schedule = RoomSchedule.Build(Monday, null, 30, [Blackout(At("09:00"), At("10:00"))]);

        schedule.Open.Should().BeNull();
        schedule.Close.Should().BeNull();
        schedule.Free.Should().BeEmpty();
        schedule.Busy.Should().ContainSingle();
    }
}
