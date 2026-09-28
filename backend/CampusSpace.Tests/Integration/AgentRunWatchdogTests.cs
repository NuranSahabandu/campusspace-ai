using CampusSpace.Api.Agents;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The poller's watchdog (plan §10.10): a run Running longer than AgentService:RunTimeoutMinutes (4) or still Queued
/// after AgentService:StartTimeoutMinutes (2) fails, so no request sticks in AgentProcessing. Time is the API's
/// TimeProvider, moved by hand.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AgentRunWatchdogTests(PostgresFixture fixture)
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task A_run_still_running_after_4_minutes_fails_and_one_just_under_is_untouched()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        var started = (await env.RunAsync(runId)).StartedAt!.Value;
        // The agent service keeps answering "running".

        env.Clock.Set(new DateTimeOffset(started, TimeSpan.Zero) + TimeSpan.FromMinutes(4) - Second);
        await env.Poller.PollOnceAsync();
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Running);

        env.Clock.Advance(2 * Second);
        await env.Poller.PollOnceAsync();

        var run = await env.RunAsync(runId);
        run.Status.Should().Be(AgentRunStatuses.Failed);
        run.FailureReason.Should().Be(AgentRunSync.TimedOutMessage);
        run.CompletedAt.Should().Be(env.Clock.GetUtcNow().UtcDateTime);
        run.DurationMs.Should().Be((int)TimeSpan.FromMinutes(4).Add(Second).TotalMilliseconds);
        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.AgentFailed);
        request.StatusHistory.OrderBy(h => h.Id).Last().Reason.Should().Be(AgentRunSync.TimedOutMessage);
    }

    [Fact]
    public async Task A_run_timing_out_during_an_outage_names_the_last_error()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (_, runId) = await env.SubmitAsync();
        env.Agent.Get = (_, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowView>("HTTP 503"));
        env.Clock.Advance(TimeSpan.FromMinutes(4) + Second);

        await env.Poller.PollOnceAsync();

        (await env.RunAsync(runId)).FailureReason.Should().Be("Agent run timed out (last error: HTTP 503)");
    }

    [Fact]
    public async Task A_run_never_started_fails_after_2_minutes_with_the_last_error_and_not_before()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        // A wrong X-Service-Key: every start is rejected, which must not look like a plain outage.
        env.Agent.Start = (_, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>("HTTP 401"));
        var (requestId, runId) = await env.SubmitAsync();
        var queued = await env.RunAsync(runId);
        queued.Status.Should().Be(AgentRunStatuses.Queued);

        env.Clock.Set(new DateTimeOffset(queued.CreatedAt, TimeSpan.Zero) + TimeSpan.FromMinutes(2) - Second);
        await env.Poller.PollOnceAsync(); // tries the start again: 401 again
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Queued);

        env.Clock.Advance(2 * Second);
        await env.Poller.PollOnceAsync();

        var run = await env.RunAsync(runId);
        run.Status.Should().Be(AgentRunStatuses.Failed);
        run.FailureReason.Should().Be("Agent service unreachable (last error: HTTP 401)");
        run.StartedAt.Should().BeNull();
        run.DurationMs.Should().BeNull();
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.AgentFailed);
        env.Agent.Calls.Count(c => c.Call == "start").Should().Be(2, "no start is tried once the run has timed out");
    }
}
