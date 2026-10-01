using CampusSpace.Api.Background;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>When the AgentRunPoller and NotificationDispatcher tick next (idle-friendly for Neon, Task 6.D3).</summary>
public class PollingScheduleTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Sweep = TimeSpan.FromMinutes(30);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 5, 0, TimeSpan.Zero);

    [Fact]
    public void While_there_is_work_the_fast_rate_is_kept() =>
        PollingSchedule.NextDelay(Now, foundWork: true, null, null, Fast, Sweep).Should().Be(Fast);

    [Fact]
    public void Idle_waits_for_the_next_clock_aligned_sweep() =>
        PollingSchedule.NextDelay(Now, foundWork: false, null, null, Fast, Sweep).Should().Be(TimeSpan.FromMinutes(25));

    [Fact]
    public void A_tick_exactly_on_a_boundary_waits_a_whole_sweep()
    {
        var boundary = new DateTimeOffset(2026, 10, 1, 10, 30, 0, TimeSpan.Zero);

        PollingSchedule.NextDelay(boundary, false, null, null, Fast, Sweep).Should().Be(Sweep);
    }

    [Fact]
    public void Sweeps_are_aligned_whatever_the_offset_of_now()
    {
        var colombo = new DateTimeOffset(2026, 10, 1, 15, 35, 0, TimeSpan.FromHours(5.5)); // = 10:05 UTC

        PollingSchedule.NextSweep(colombo, Sweep).Should().Be(new DateTimeOffset(2026, 10, 1, 10, 30, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    public void Within_the_grace_after_a_signal_the_fast_rate_is_kept(int secondsAgo) =>
        PollingSchedule.NextDelay(Now, false, Now.AddSeconds(-secondsAgo), null, Fast, Sweep).Should().Be(Fast);

    [Fact]
    public void After_the_grace_a_signal_no_longer_keeps_it_fast() =>
        PollingSchedule.NextDelay(Now, false, Now - PollingSchedule.ActiveGrace, null, Fast, Sweep)
            .Should().Be(TimeSpan.FromMinutes(25));

    [Fact]
    public void A_due_time_before_the_sweep_wins() =>
        PollingSchedule.NextDelay(Now, false, null, Now.AddMinutes(10), Fast, Sweep).Should().Be(TimeSpan.FromMinutes(10));

    [Fact]
    public void A_due_time_after_the_sweep_does_not_delay_the_sweep() =>
        PollingSchedule.NextDelay(Now, false, null, Now.AddHours(2), Fast, Sweep).Should().Be(TimeSpan.FromMinutes(25));

    [Theory]
    [InlineData(0)]
    [InlineData(-90)]
    public void Something_already_due_is_polled_at_the_fast_rate(int seconds) =>
        PollingSchedule.NextDelay(Now, false, null, Now.AddSeconds(seconds), Fast, Sweep).Should().Be(Fast);
}
