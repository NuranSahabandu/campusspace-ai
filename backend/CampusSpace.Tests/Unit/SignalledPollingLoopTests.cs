using CampusSpace.Api.Background;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CampusSpace.Tests.Unit;

/// <summary>
/// The shared background loop over a FakeTimeProvider: time only moves when a test advances it, so "no tick between
/// sweeps" is checked without sleeping for 30 minutes. A tick here stands for one database poll.
/// </summary>
public sealed class SignalledPollingLoopTests : IAsyncDisposable
{
    private static readonly TimeSpan Fast = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Sweep = TimeSpan.FromMinutes(30);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 10, 5, 0, TimeSpan.Zero));
    private readonly CancellationTokenSource _stop = new();
    private readonly WorkSignal _signal;
    private Task? _loop;
    private int _ticks;
    private volatile bool _work;
    private volatile bool _fail;

    public SignalledPollingLoopTests() => _signal = new WorkSignal(_clock);

    private int Ticks => Volatile.Read(ref _ticks);

    private void Start() => _loop = SignalledPollingLoop.RunAsync(_ =>
        {
            Interlocked.Increment(ref _ticks);
            return _fail ? throw new InvalidOperationException("database down") : Task.FromResult(new TickResult(_work));
        },
        _signal, Fast, Sweep, _clock, NullLogger.Instance, "test", _stop.Token);

    /// <summary>Waits (real time) until the loop has ticked <paramref name="count"/> times and is waiting again.</summary>
    private async Task TicksReach(int count)
    {
        for (var i = 0; i < 200 && Ticks < count; i++)
            await Task.Delay(10);
        Ticks.Should().Be(count);
        await Task.Delay(50); // lets the loop schedule its wait before the test moves the clock
    }

    private async Task Advance(TimeSpan by)
    {
        _clock.Advance(by);
        await Task.Delay(50);
    }

    [Fact]
    public async Task Idle_it_ticks_once_at_start_then_not_again_until_the_aligned_sweep()
    {
        Start();
        await TicksReach(1); // the restart sweep: leftovers are picked up at once

        await Advance(TimeSpan.FromMinutes(24)); // 10:29
        Ticks.Should().Be(1, "an idle loop does not poll the database between sweeps");

        await Advance(TimeSpan.FromMinutes(1)); // 10:30, the boundary
        await TicksReach(2);
        await Advance(TimeSpan.FromMinutes(29));
        Ticks.Should().Be(2);
    }

    [Fact]
    public async Task A_signal_wakes_an_idle_loop_at_once_and_keeps_it_fast_for_the_grace()
    {
        Start();
        await TicksReach(1);

        _signal.Notify(); // without moving the clock
        await TicksReach(2);
        await Advance(Fast);
        await TicksReach(3); // the commit may come a moment after the signal: keep polling fast

        await Advance(PollingSchedule.ActiveGrace);
        await TicksReach(4);
        await Advance(TimeSpan.FromMinutes(10));
        Ticks.Should().Be(4, "after the grace it is idle again");
    }

    [Fact]
    public async Task While_there_is_work_it_ticks_at_the_fast_rate()
    {
        _work = true;
        Start();
        await TicksReach(1);

        await Advance(Fast);
        await TicksReach(2);
        await Advance(Fast);
        await TicksReach(3);

        _work = false;
        await Advance(Fast);
        await TicksReach(4);
        await Advance(TimeSpan.FromMinutes(5));
        Ticks.Should().Be(4);
    }

    [Fact]
    public async Task A_failed_tick_is_retried_at_the_fast_rate()
    {
        _fail = true;
        Start();
        await TicksReach(1);

        await Advance(Fast);
        await TicksReach(2);
    }

    [Fact]
    public async Task Stopping_ends_the_loop()
    {
        Start();
        await TicksReach(1);

        await _stop.CancelAsync();

        await FluentActions.Awaiting(() => _loop!).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_signal_raised_while_nobody_waits_is_not_lost()
    {
        var signal = new WorkSignal(_clock);
        signal.Notify();

        (await signal.WaitAsync(Sweep, CancellationToken.None)).Should().BeTrue();
        var second = signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        await Task.Delay(20);
        _clock.Advance(TimeSpan.FromSeconds(1));
        (await second).Should().BeFalse("one notify wakes one wait");
        signal.Count.Should().Be(1);
        signal.LastNotifiedAt.Should().Be(_clock.GetUtcNow() - TimeSpan.FromSeconds(1));
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
            await _loop.ContinueWith(_ => { }, TaskScheduler.Default);
        _stop.Dispose();
    }
}
