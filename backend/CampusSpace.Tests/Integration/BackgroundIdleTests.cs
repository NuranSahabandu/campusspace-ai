using System.Net.Http.Json;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Background;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Notifications;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The idle-friendly background loops (Task 6.D3): the API's own writes wake them at once (AppDbContext raises
/// WorkSignals), the email dispatcher knows when a row is next due, and a fresh host picks up work left from before.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BackgroundIdleTests(PostgresFixture fixture)
{
    private static WorkSignals Signals(WebApplicationFactory<Program> factory) => factory.Services.GetRequiredService<WorkSignals>();

    [Fact]
    public async Task A_submit_signals_the_agent_poller()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var before = Signals(env.Factory).Agent.Count;

        await env.SubmitAsync();

        Signals(env.Factory).Agent.Count.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task An_approve_signals_the_agent_poller_and_a_reject_signals_the_email_dispatcher()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (approved, _) = await env.ToPendingApprovalAsync();
        var (rejected, _) = await env.ToPendingApprovalAsync(weekdaysAhead: 25);
        var agent = Signals(env.Factory).Agent.Count;

        (await env.DecideAsync(approved, "approve")).EnsureSuccessStatusCode(); // run → Resuming, then Completed
        Signals(env.Factory).Agent.Count.Should().BeGreaterThan(agent);

        var email = Signals(env.Factory).Email.Count;
        (await env.DecideAsync(rejected, "reject", new { reason = "The hall is reserved for exams." })).EnsureSuccessStatusCode();
        Signals(env.Factory).Email.Count.Should().BeGreaterThan(email);
    }

    [Fact]
    public async Task The_dispatcher_waits_for_a_retry_that_is_due_later_instead_of_polling()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "reject", new { reason = "Closed for maintenance." })).EnsureSuccessStatusCode();
        var dispatcher = env.Factory.Services.GetRequiredService<NotificationDispatcher>();
        var now = env.Clock.GetUtcNow();

        (await dispatcher.NextDueAsync()).Should().Be(now, "a new Pending row is due now");

        var later = now.AddMinutes(10);
        await env.QueryAsync(db => db.NotificationLogs.Where(n => n.Status == NotificationStatuses.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, later.UtcDateTime)));
        (await dispatcher.ProcessOnceAsync()).Should().Be(0);
        var due = await dispatcher.NextDueAsync();

        due.Should().Be(later);
        // A long sweep, so the retry (not a sweep boundary that happens to be near) decides the wait.
        var sweep = TimeSpan.FromDays(7);
        var expected = PollingSchedule.NextSweep(now, sweep) - now < TimeSpan.FromMinutes(10)
            ? PollingSchedule.NextSweep(now, sweep) - now
            : TimeSpan.FromMinutes(10);
        PollingSchedule.NextDelay(now, false, null, due, TimeSpan.FromSeconds(5), sweep).Should().Be(expected);
    }

    [Fact]
    public async Task With_nothing_pending_the_dispatcher_has_no_due_time()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);

        (await env.Factory.Services.GetRequiredService<NotificationDispatcher>().NextDueAsync()).Should().BeNull();
    }

    [Fact]
    public async Task A_fresh_hosted_poller_picks_up_a_run_queued_before_it_started()
    {
        // A restart: the run was queued by another host (its signal went nowhere); the new poller's first tick finds it,
        // although its idle sweep is a day away.
        await using var isolated = await fixture.CreateIsolatedFactoryAsync();
        isolated.AgentClient.Start = (_, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());
        var (client, _, clubId) = await StudentRepAsync(isolated);
        var id = (await (await client.PostAsJsonAsync(Url, Body(clubId))).ReadJsonAsync()).GetProperty("id").GetInt64();
        isolated.AgentClient.Start = (thread, _, _) => Task.FromResult(FakeAgentClient.Accepted(thread));

        await using var restarted = isolated.WithWebHostBuilder(b => b
            .UseSetting("AgentService:PollerEnabled", "true").UseSetting("AgentService:PollSeconds", "1")
            .UseSetting("AgentService:IdleSweepMinutes", "1440"));
        _ = restarted.Server;

        (await WaitForRunStatusAsync(isolated, id, AgentRunStatuses.Running)).Should().Be(AgentRunStatuses.Running);
    }

    [Fact]
    public async Task An_idle_hosted_poller_is_woken_at_once_by_a_submit()
    {
        await using var isolated = await fixture.CreateIsolatedFactoryAsync();
        await using var host = isolated.WithWebHostBuilder(b => b
            .UseSetting("AgentService:PollerEnabled", "true").UseSetting("AgentService:PollSeconds", "1")
            .UseSetting("AgentService:IdleSweepMinutes", "1440"));
        var starts = 0;
        // The inline start fails (the agent is "asleep"); every later start is accepted.
        isolated.AgentClient.Start = (thread, _, _) => Task.FromResult(Interlocked.Increment(ref starts) == 1
            ? FakeAgentClient.Unavailable<AgentWorkflowAccepted>("timeout")
            : FakeAgentClient.Accepted(thread));
        var (rep, _, clubId) = await StudentRepAsync(isolated);
        var client = host.SameUser(rep); // starts the host: the poller ticks once and goes idle
        await Task.Delay(300);

        var id = (await (await client.PostAsJsonAsync(Url, Body(clubId))).ReadJsonAsync()).GetProperty("id").GetInt64();

        (await WaitForRunStatusAsync(isolated, id, AgentRunStatuses.Running)).Should().Be(AgentRunStatuses.Running,
            "the submit's signal wakes the poller although its next sweep is a day away");
    }

    [Fact]
    public async Task An_idle_hosted_dispatcher_is_woken_at_once_by_a_new_email()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        await using var host = env.Factory.WithWebHostBuilder(b => b
            .UseSetting("Email:DispatcherEnabled", "true").UseSetting("Email:PollSeconds", "1")
            .UseSetting("Email:IdleSweepMinutes", "1440"));
        var officer = host.SameUser((await env.OfficerAsync()).Client); // starts the dispatcher
        await Task.Delay(300);

        (await officer.PostAsJsonAsync($"{Url}/{requestId}/reject", new { reason = "Closed for maintenance." }))
            .EnsureSuccessStatusCode();

        string? status = null;
        for (var i = 0; i < 50 && status is null or NotificationStatuses.Pending; i++)
        {
            await Task.Delay(100);
            status = await env.QueryAsync(db => db.NotificationLogs.Where(n => n.RequestId == requestId)
                .Select(n => n.Status).SingleOrDefaultAsync());
        }
        status.Should().Be(NotificationStatuses.Skipped, "no Brevo key in tests: the woken dispatcher records Skipped");
    }

    private static async Task<string> WaitForRunStatusAsync(WebApplicationFactory<Program> factory, long requestId, string wanted)
    {
        var status = "";
        for (var i = 0; i < 50 && status != wanted; i++)
        {
            await Task.Delay(100);
            await using var scope = factory.Services.CreateAsyncScope();
            status = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentRuns
                .Where(r => r.RequestId == requestId).Select(r => r.Status).SingleAsync();
        }
        return status;
    }
}
