namespace CampusSpace.Api.Extensions;

/// <summary>
/// Campus local time (Asia/Colombo). Sri Lanka has no daylight saving, so it is always UTC+05:30.
/// Timestamps are stored in UTC; use this only where a rule is about a campus calendar date or clock time.
/// </summary>
public static class CampusTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    /// <summary>Today's date on campus.</summary>
    public static DateOnly Today(TimeProvider clock) => DateOf(clock.GetUtcNow());

    /// <summary>The campus calendar date of an instant: 00:30 on campus is still the previous day in UTC.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);
}
