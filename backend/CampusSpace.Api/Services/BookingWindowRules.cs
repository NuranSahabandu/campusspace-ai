using System.Globalization;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

public sealed class BookingWindowRules(TimeProvider clock) : IBookingWindowRules
{
    public const string EndAfterStartMessage = "End must be after start";
    public const string SameDayMessage = "Must end on the same day as the start";
    public const string FutureMessage = "Start must be in the future.";

    public BookingWindowErrors CheckSlot(DateTimeOffset start, DateTimeOffset end, PolicySnapshot policy)
    {
        var date = CampusTime.DateOf(start);
        var hours = policy.OpeningHours[date.DayOfWeek];
        var startTime = TimeOf(start);
        var endTime = TimeOf(end);
        var granularity = TimeSpan.FromMinutes(policy.SlotGranularityMinutes);
        var onBoundary = $"Must be on a {policy.SlotGranularityMinutes}-minute boundary";
        // "Saturdays": the same wording as the mobile form (formatWeekday + "s").
        var days = $"{date.DayOfWeek}s";

        string? startError = null;
        if (hours is null)
            startError = $"The campus is closed on {days}";
        else if (!IsOnBoundary(start, granularity))
            startError = onBoundary;
        else if (startTime < hours.Open)
            startError = $"Opens at {Format(hours.Open)} on {days}";
        else if (startTime >= hours.Close)
            startError = $"Closes at {Format(hours.Close)} on {days}";

        string? endError = null;
        if (!IsOnBoundary(end, granularity))
            endError = onBoundary;
        else if (end <= start)
            endError = EndAfterStartMessage;
        else if (CampusTime.DateOf(end) != date)
            endError = SameDayMessage;
        else if (hours is not null && endTime > hours.Close)
            endError = $"Must end by {Format(hours.Close)} on {days}";
        else if (end - start > TimeSpan.FromHours(policy.MaxDurationHours))
            endError = $"Bookings can be at most {policy.MaxDurationHours} hours";

        return new BookingWindowErrors(startError, endError);
    }

    public BookingWindowErrors CheckTiming(DateTimeOffset start, string requesterRole, PolicySnapshot policy)
    {
        var now = clock.GetUtcNow();
        var maxDays = requesterRole == Roles.Lecturer ? policy.MaxAdvanceDaysLecturer : policy.MaxAdvanceDaysStudent;

        if (start <= now)
            return new BookingWindowErrors(FutureMessage, null);
        if (start < now.AddHours(policy.MinLeadTimeHours))
            return new BookingWindowErrors($"Must start at least {policy.MinLeadTimeHours} hours from now", null);
        if (CampusTime.DateOf(start) > CampusTime.Today(clock).AddDays(maxDays))
            return new BookingWindowErrors($"Can be booked at most {maxDays} days ahead", null);
        return BookingWindowErrors.None;
    }

    public BookingWindowErrors Check(DateTimeOffset start, DateTimeOffset end, string requesterRole, PolicySnapshot policy) =>
        CheckSlot(start, end, policy).Then(CheckTiming(start, requesterRole, policy));

    /// <summary>Campus clock time. Midnight at the end of a day reads as 00:00, which the same-day rule rejects.</summary>
    private static TimeOnly TimeOf(DateTimeOffset instant) => TimeOnly.FromTimeSpan(instant.ToOffset(CampusTime.Offset).TimeOfDay);

    /// <summary>Boundaries count from campus midnight, and seconds or smaller units must be zero.</summary>
    private static bool IsOnBoundary(DateTimeOffset instant, TimeSpan granularity) =>
        instant.ToOffset(CampusTime.Offset).TimeOfDay.Ticks % granularity.Ticks == 0;

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
