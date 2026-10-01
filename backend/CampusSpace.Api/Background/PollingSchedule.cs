namespace CampusSpace.Api.Background;

/// <summary>
/// When a background loop ticks next (Task 6.D3). While there is work it keeps its fast rate (the AgentRunPoller's 3 s,
/// the NotificationDispatcher's 5 s), exactly as before. When idle it does not query the database every few seconds,
/// which would keep Neon's compute awake (and spend the free plan's CU-hours) for as long as the API is up: it waits
/// for a signal (new work created in this process), a known due time, or the next idle sweep.
/// </summary>
public static class PollingSchedule
{
    /// <summary>
    /// Fast polling continues this long after a signal, even when a tick finds nothing: a signal can be raised by a
    /// SaveChanges inside a transaction that commits a moment later, so the first tick may not see the new row yet.
    /// </summary>
    public static readonly TimeSpan ActiveGrace = TimeSpan.FromSeconds(60);

    /// <param name="foundWork">The last tick found something to process.</param>
    /// <param name="lastSignal">The loop's <see cref="WorkSignal.LastNotifiedAt"/>.</param>
    /// <param name="nextDue">The earliest known future due time (for example a Pending email's NextAttemptAt).</param>
    public static TimeSpan NextDelay(DateTimeOffset now, bool foundWork, DateTimeOffset? lastSignal, DateTimeOffset? nextDue,
        TimeSpan fast, TimeSpan sweep)
    {
        if (foundWork || (lastSignal is { } signal && now - signal < ActiveGrace))
            return fast;
        var delay = NextSweep(now, sweep) - now;
        if (nextDue is { } due)
        {
            if (due <= now)
                return fast;
            if (due - now < delay)
                delay = due - now;
        }
        return delay;
    }

    /// <summary>
    /// The next sweep: the first multiple of <paramref name="sweep"/> (counted from the Unix epoch) strictly after
    /// <paramref name="now"/>. Aligned to the clock, so the poller's and the dispatcher's sweeps wake Neon together.
    /// </summary>
    public static DateTimeOffset NextSweep(DateTimeOffset now, TimeSpan sweep)
    {
        var sinceEpoch = now.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var next = (sinceEpoch / sweep.Ticks + 1) * sweep.Ticks;
        return DateTimeOffset.UnixEpoch.AddTicks(next);
    }
}
