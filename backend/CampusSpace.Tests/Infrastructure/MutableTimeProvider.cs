namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// A clock a test moves by hand, for the agent run watchdog: it starts at the real "now" (row CreatedAt values come
/// from the database clock) and is then advanced past or just short of a timeout. Times are cut to whole microseconds,
/// PostgreSQL's timestamptz precision, so a value read back from the database equals the clock exactly (Linux clocks
/// have 100 ns ticks).
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = ToMicroseconds(now);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Set(DateTimeOffset now) => _now = ToMicroseconds(now);

    public void Advance(TimeSpan by) => _now = ToMicroseconds(_now + by);

    private static DateTimeOffset ToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, value.Offset);
}
