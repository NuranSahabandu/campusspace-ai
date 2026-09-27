using System.Collections.Immutable;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>
/// V05/V06 with a frozen clock and an explicit policy, so each test states the values it relies on.
/// "Now" is Wednesday 2026-10-07 10:00 campus time.
/// </summary>
public class BookingWindowRulesTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 7);
    private static readonly DateOnly Saturday = new(2026, 10, 10);
    private static readonly DateOnly Sunday = new(2026, 10, 11);
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static readonly BookingWindowRules Rules = new(new FixedTimeProvider(At(Wednesday, "10:00")));

    /// <summary>Mon–Fri 08:00–20:00, Sat 08:00–16:00, Sun closed; 48 h lead; 60/90 days; 8 h; 30-minute slots.</summary>
    private static PolicySnapshot Policy(int granularity = 30, int leadHours = 48, int maxHours = 8)
    {
        var weekday = new DayHours(new TimeOnly(8, 0), new TimeOnly(20, 0));
        var hours = new Dictionary<DayOfWeek, DayHours?>
        {
            [DayOfWeek.Monday] = weekday, [DayOfWeek.Tuesday] = weekday, [DayOfWeek.Wednesday] = weekday,
            [DayOfWeek.Thursday] = weekday, [DayOfWeek.Friday] = weekday,
            [DayOfWeek.Saturday] = new DayHours(new TimeOnly(8, 0), new TimeOnly(16, 0)),
            [DayOfWeek.Sunday] = null,
        }.ToImmutableDictionary();
        return new PolicySnapshot(hours, leadHours, MaxAdvanceDaysStudent: 60, MaxAdvanceDaysLecturer: 90, maxHours,
            MaxCapacityRatio: 3, granularity, FreeCancellationHours: 24, MaxOpenRequests: 3);
    }

    private static DateTimeOffset At(DateOnly date, string time) => CampusTime.At(date, TimeOnly.Parse(time));

    private static BookingWindowErrors Slot(DateOnly date, string from, string to, PolicySnapshot? policy = null) =>
        Rules.CheckSlot(At(date, from), At(date, to), policy ?? Policy());

    [Fact]
    public void A_weekday_slot_inside_opening_hours_passes()
    {
        Slot(Monday, "14:00", "17:00").Should().Be(BookingWindowErrors.None);
        Slot(Monday, "08:00", "16:00").IsValid.Should().BeTrue("opening to 8 h later is exactly the maximum");
        Slot(Monday, "19:30", "20:00").IsValid.Should().BeTrue("ending exactly at closing time is allowed");
    }

    [Fact]
    public void Sunday_is_closed()
    {
        Slot(Sunday, "10:00", "12:00").Should().Be(new BookingWindowErrors("The campus is closed on Sundays", null));
    }

    [Fact]
    public void Saturday_closes_at_16_00()
    {
        Slot(Saturday, "14:00", "16:00").IsValid.Should().BeTrue();
        Slot(Saturday, "15:00", "17:00").End.Should().Be("Must end by 16:00 on Saturdays");
        Slot(Saturday, "16:00", "17:00").Start.Should().Be("Closes at 16:00 on Saturdays");
    }

    [Fact]
    public void A_start_before_opening_is_refused()
    {
        Slot(Monday, "07:30", "09:00").Start.Should().Be("Opens at 08:00 on Mondays");
    }

    [Fact]
    public void Times_off_the_granularity_are_refused()
    {
        Slot(Monday, "14:15", "16:00").Start.Should().Be("Must be on a 30-minute boundary");
        Slot(Monday, "14:00", "16:10").End.Should().Be("Must be on a 30-minute boundary");
        Rules.CheckSlot(At(Monday, "14:00").AddSeconds(1), At(Monday, "15:00"), Policy()).Start
            .Should().Be("Must be on a 30-minute boundary");
    }

    [Fact]
    public void A_policy_with_60_minute_slots_refuses_half_hours()
    {
        Slot(Monday, "14:30", "16:00").IsValid.Should().BeTrue();
        Slot(Monday, "14:30", "16:00", Policy(granularity: 60)).Start.Should().Be("Must be on a 60-minute boundary");
    }

    [Fact]
    public void Bookings_longer_than_the_maximum_are_refused()
    {
        Slot(Monday, "08:00", "16:30").End.Should().Be("Bookings can be at most 8 hours");
        Slot(Monday, "08:00", "16:30", Policy(maxHours: 9)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void End_must_be_after_start()
    {
        Slot(Monday, "14:00", "14:00").End.Should().Be("End must be after start");
        Slot(Monday, "15:00", "14:00").End.Should().Be("End must be after start");
    }

    [Fact]
    public void A_booking_cannot_cross_campus_midnight()
    {
        var errors = Rules.CheckSlot(At(Monday, "19:00"), At(Monday.AddDays(1), "01:00"), Policy());

        errors.End.Should().Be("Must end on the same day as the start");
    }

    [Fact]
    public void Campus_dates_decide_the_day_not_utc_dates()
    {
        // Monday 08:00 campus time is Monday 02:30 UTC, and Tuesday 01:00 campus time is still Monday in UTC.
        Rules.CheckSlot(new DateTimeOffset(2026, 10, 12, 2, 30, 0, TimeSpan.Zero), At(Monday, "09:00"), Policy())
            .IsValid.Should().BeTrue();
        Rules.CheckSlot(At(Monday, "19:00"), new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero), Policy())
            .End.Should().Be("Must end on the same day as the start");
    }

    [Fact]
    public void Lead_time_passes_exactly_at_the_boundary_and_fails_a_slot_earlier()
    {
        var now = At(Wednesday, "10:00");

        Rules.CheckTiming(now.AddHours(48), Roles.Student, Policy()).IsValid.Should().BeTrue();
        Rules.CheckTiming(now.AddHours(48).AddMinutes(-30), Roles.Student, Policy()).Start
            .Should().Be("Must start at least 48 hours from now");
        Rules.CheckTiming(now.AddHours(24), Roles.Student, Policy(leadHours: 24)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_start_in_the_past_or_now_must_be_in_the_future()
    {
        Rules.CheckTiming(At(Wednesday, "10:00"), Roles.Student, Policy()).Start.Should().Be("Start must be in the future.");
        Rules.CheckTiming(At(Wednesday, "08:00"), Roles.Student, Policy(leadHours: 0)).Start.Should().Be("Start must be in the future.");
    }

    [Fact]
    public void The_advance_window_is_chosen_by_role()
    {
        // Today + 60 is Sunday 2026-12-06; + 61 is Monday.
        var day60 = At(Wednesday.AddDays(60), "10:00");
        var day61 = At(Wednesday.AddDays(61), "10:00");

        Rules.CheckTiming(day60, Roles.Student, Policy()).IsValid.Should().BeTrue();
        Rules.CheckTiming(day61, Roles.Student, Policy()).Start.Should().Be("Can be booked at most 60 days ahead");
        Rules.CheckTiming(day61, Roles.Lecturer, Policy()).IsValid.Should().BeTrue();
        Rules.CheckTiming(At(Wednesday.AddDays(91), "10:00"), Roles.Lecturer, Policy()).Start
            .Should().Be("Can be booked at most 90 days ahead");
    }

    [Fact]
    public void Check_reports_slot_rules_before_timing_and_one_message_per_field()
    {
        // Thursday 07:00 is before opening and also inside the lead time: the opening-hours message wins, as on mobile.
        var early = At(Wednesday.AddDays(1), "07:00");
        Rules.Check(early, early.AddHours(2), Roles.Student, Policy()).Start.Should().Be("Opens at 08:00 on Thursdays");

        // Thursday 10:00 is a valid slot but only 24 h away.
        var thursday = At(Wednesday.AddDays(1), "10:00");
        Rules.Check(thursday, thursday.AddHours(9), Roles.Student, Policy())
            .Should().Be(new BookingWindowErrors("Must start at least 48 hours from now", "Bookings can be at most 8 hours"));
    }

    [Fact]
    public void Field_errors_use_the_request_names_by_default_and_the_callers_names_when_given()
    {
        var errors = new BookingWindowErrors("a", "b");

        errors.ToFieldErrors().Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["RequestedStart"] = ["a"], ["RequestedEnd"] = ["b"],
        });
        new BookingWindowErrors(null, "b").ToFieldErrors("Start", "End").Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["End"] = ["b"],
        });
    }
}
