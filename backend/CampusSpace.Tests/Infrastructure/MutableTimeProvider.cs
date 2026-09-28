namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// A clock a test moves by hand, for the agent run watchdog: it starts at the real "now" (row CreatedAt values come
/// from the database clock) and is then advanced past or just short of a timeout.
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Set(DateTimeOffset now) => _now = now;

    public void Advance(TimeSpan by) => _now += by;
}
