using CampusSpace.Api.Extensions;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

public class CampusTimeTests
{
    [Fact]
    public void Half_past_midnight_on_campus_is_the_previous_day_in_utc_but_the_campus_date_is_today()
    {
        // 2026-03-10 00:30 at +05:30 is 2026-03-09 19:00 UTC.
        var instant = new DateTimeOffset(2026, 3, 9, 19, 0, 0, TimeSpan.Zero);

        CampusTime.DateOf(instant).Should().Be(new DateOnly(2026, 3, 10));
        CampusTime.Today(new FixedTimeProvider(instant)).Should().Be(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public void Just_before_campus_midnight_is_still_the_same_day()
    {
        // 2026-03-09 23:59 at +05:30 is 18:29 UTC.
        CampusTime.DateOf(new DateTimeOffset(2026, 3, 9, 18, 29, 0, TimeSpan.Zero)).Should().Be(new DateOnly(2026, 3, 9));
    }
}
