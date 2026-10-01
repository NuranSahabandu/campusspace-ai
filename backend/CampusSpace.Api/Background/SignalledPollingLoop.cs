namespace CampusSpace.Api.Background;

/// <summary>What one tick found: whether it had work, and the earliest known future due time.</summary>
public readonly record struct TickResult(bool FoundWork, DateTimeOffset? NextDueAt = null);

/// <summary>
/// The loop shared by the AgentRunPoller and the NotificationDispatcher: tick, then wait as long as
/// <see cref="PollingSchedule.NextDelay"/> says or until the signal fires. The first tick runs at once, so work left
/// over from before a restart is picked up on startup. A failed tick is logged and retried at the fast rate.
/// </summary>
public static class SignalledPollingLoop
{
    public static async Task RunAsync(Func<CancellationToken, Task<TickResult>> tick, WorkSignal signal, TimeSpan fast,
        TimeSpan sweep, TimeProvider clock, ILogger logger, string name, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TickResult result;
            try
            {
                result = await tick(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "{Loop} tick failed; retrying on the next tick", name);
                result = new TickResult(FoundWork: true);
            }
            var delay = PollingSchedule.NextDelay(clock.GetUtcNow(), result.FoundWork, signal.LastNotifiedAt,
                result.NextDueAt, fast, sweep);
            await signal.WaitAsync(delay, ct);
        }
    }
}
