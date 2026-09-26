namespace CampusSpace.Tests.Infrastructure;

/// <summary>A clock frozen at one instant, for minting tokens in the past or checking exact expiry.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
