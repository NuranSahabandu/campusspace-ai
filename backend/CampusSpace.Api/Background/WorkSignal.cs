namespace CampusSpace.Api.Background;

/// <summary>
/// An in-process "there is new work" signal for one background loop. <see cref="Notify"/> wakes a waiting
/// <see cref="WaitAsync"/> at once; a notify while nobody waits is kept, so the next wait returns at once (nothing is
/// lost between a tick and its wait). Times come from the TimeProvider, so tests can drive the timeout.
/// </summary>
public sealed class WorkSignal(TimeProvider clock)
{
    private readonly object _gate = new();
    private TaskCompletionSource _wake = NewSource();
    private long _lastNotifiedTicks; // UTC ticks; 0 = never
    private long _count;

    /// <summary>When the last notify happened (null = never). The loop polls fast for a grace period after it.</summary>
    public DateTimeOffset? LastNotifiedAt =>
        Interlocked.Read(ref _lastNotifiedTicks) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    /// <summary>How many notifies there have been (for tests and diagnostics).</summary>
    public long Count => Interlocked.Read(ref _count);

    public void Notify()
    {
        Interlocked.Exchange(ref _lastNotifiedTicks, clock.GetUtcNow().UtcTicks);
        Interlocked.Increment(ref _count);
        lock (_gate)
            _wake.TrySetResult();
    }

    /// <summary>Waits until a notify or <paramref name="timeout"/>. True when woken by a notify.</summary>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        Task wake;
        lock (_gate)
        {
            if (_wake.Task.IsCompleted)
            {
                _wake = NewSource();
                return true;
            }
            wake = _wake.Task;
        }

        using var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout > TimeSpan.Zero ? timeout : TimeSpan.Zero, clock, delayCancel.Token);
        var first = await Task.WhenAny(wake, delay);
        delayCancel.Cancel(); // stops the timer when the notify won
        ct.ThrowIfCancellationRequested();
        if (first != wake)
            return false;
        lock (_gate)
            if (_wake.Task == wake)
                _wake = NewSource();
        return true;
    }

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>The two loops' signals (singleton). AppDbContext raises them after a save that created work.</summary>
public sealed class WorkSignals(TimeProvider clock)
{
    /// <summary>A run was added or moved to Queued, Running or Resuming: the AgentRunPoller has work.</summary>
    public WorkSignal Agent { get; } = new(clock);

    /// <summary>A NotificationLogs row was added: the NotificationDispatcher has work.</summary>
    public WorkSignal Email { get; } = new(clock);
}
