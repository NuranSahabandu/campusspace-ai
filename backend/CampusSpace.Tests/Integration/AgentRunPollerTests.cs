using System.Net.Http.Json;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// AgentRunPoller over the fake agent service, whose views are verbatim 3.2 responses (Fixtures/). Each test has its
/// own seeded database, because a tick processes every live run in it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AgentRunPollerTests(PostgresFixture fixture)
{
    private static readonly AgentWorkflowView Awaiting = AgentFixtures.View(AgentFixtures.AwaitingStudent);

    [Fact]
    public async Task A_queued_run_is_started_on_the_next_tick()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        env.Agent.Start = (_, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());
        var (_, runId) = await env.SubmitAsync();
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Queued);

        env.Agent.Start = (thread, _, _) => Task.FromResult(FakeAgentClient.Accepted(thread));
        await env.Poller.PollOnceAsync();

        var run = await env.RunAsync(runId);
        run.Status.Should().Be(AgentRunStatuses.Running);
        run.StartedAt.Should().Be(env.Clock.GetUtcNow().UtcDateTime);
        env.Agent.Calls.Count(c => c == ("start", runId, null)).Should().Be(2);
    }

    [Fact]
    public async Task When_enabled_the_hosted_poller_ticks_on_its_own()
    {
        await using var isolated = await fixture.CreateIsolatedFactoryAsync();
        await using var factory = isolated.WithWebHostBuilder(b => b
            .UseSetting("AgentService:PollerEnabled", "true").UseSetting("AgentService:PollSeconds", "1"));
        isolated.AgentClient.Start = (_, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());
        var (client, _, clubId) = await StudentRepAsync(isolated);
        var id = (await (await client.PostAsJsonAsync(Url, Body(clubId))).ReadJsonAsync()).GetProperty("id").GetInt64();
        _ = factory.Server; // starts the host, and with it the poller

        isolated.AgentClient.Start = (thread, _, _) => Task.FromResult(FakeAgentClient.Accepted(thread));
        var status = AgentRunStatuses.Queued;
        for (var i = 0; i < 50 && status == AgentRunStatuses.Queued; i++)
        {
            await Task.Delay(200);
            await using var scope = isolated.Services.CreateAsyncScope();
            status = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentRuns
                .Where(r => r.RequestId == id).Select(r => r.Status).SingleAsync();
        }

        status.Should().Be(AgentRunStatuses.Running);
    }

    [Fact]
    public async Task Awaiting_approval_copies_the_trace_drafts_the_5500_quote_and_moves_the_request_to_PendingApproval()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        // The fixture comes from the agent service's seed-shaped fake, where A301 is room 1; so is it in the seed.
        (await env.QueryAsync(db => db.Rooms.Where(r => r.Code == "A301").Select(r => r.Id).SingleAsync()))
            .Should().Be(Awaiting.ReadProposal()!.RoomId);
        env.AgentReturns(Awaiting);

        await env.Poller.PollOnceAsync();

        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.PendingApproval);
        var last = request.StatusHistory.OrderBy(h => h.Id).Last();
        (last.FromStatus, last.ToStatus, last.ChangedById).Should().Be((RequestStatuses.AgentProcessing, RequestStatuses.PendingApproval, (long?)null));

        var run = await env.RunAsync(runId);
        run.Status.Should().Be(AgentRunStatuses.AwaitingApproval);
        run.Nodes.Should().Equal(Awaiting.Nodes);
        run.PlanJson.Should().NotBeNullOrEmpty();
        run.ProposalJson.Should().Contain("\"room_code\"").And.Contain("A301");
        run.PolicySnapshotJson.Should().NotBeNullOrEmpty();
        run.OfficerSummary.Should().Be(Awaiting.OfficerSummary).And.NotBeNullOrEmpty();
        run.Model.Should().Be("stub");
        run.DurationMs.Should().NotBeNull();
        run.CompletedAt.Should().BeNull("a paused run is not complete");

        var steps = await env.QueryAsync(db => db.AgentSteps.AsNoTracking().Include(s => s.ToolCalls)
            .Where(s => s.RunId == runId).OrderBy(s => s.Sequence).ToListAsync());
        steps.Select(s => (s.Sequence, s.AgentName)).Should().Equal(Awaiting.Steps!.Select(s => (s.Sequence, s.AgentName)));
        steps.Sum(s => s.ToolCalls.Count).Should().Be(Awaiting.Steps!.Sum(s => s.ToolCalls!.Count)).And.BePositive();
        steps.SelectMany(s => s.ToolCalls).Should().OnlyContain(c => c.ArgsJson.Length > 0 && c.DurationMs >= 0);
        (await env.QueryAsync(db => db.ValidationResults.CountAsync(v => v.RunId == runId && v.Passed && v.Attempt == 1)))
            .Should().Be(12);

        var quote = await env.QueryAsync(db => db.Quotations.AsNoTracking().Include(q => q.Lines).SingleAsync(q => q.RequestId == requestId));
        quote.Status.Should().Be(QuotationStatuses.Draft);
        quote.AgentRunId.Should().Be(runId);
        (quote.Subtotal, quote.Discount, quote.Total, quote.IsExempt).Should().Be((5500.00m, 0m, 5500.00m, false));
        // Room 3 h × 1,500 and 2 × MIC-WIRELESS × 500; the projector is covered by A301 (room_builtin), so unpriced.
        quote.Lines.Select(l => (l.Kind, l.Qty, l.LineTotal)).Should().BeEquivalentTo(
            [(QuotationLineKinds.Room, 3m, 4500.00m), (QuotationLineKinds.Equipment, 2m, 1000.00m)]);
    }

    [Fact]
    public async Task A_lecturers_draft_quote_is_exempt_with_a_total_of_zero()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync(lecturer: true);
        env.AgentReturns(AgentFixtures.View(AgentFixtures.AwaitingLecturer));

        await env.Poller.PollOnceAsync();

        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.PendingApproval);
        var quote = await env.QueryAsync(db => db.Quotations.AsNoTracking().SingleAsync(q => q.RequestId == requestId));
        quote.IsExempt.Should().BeTrue();
        quote.Total.Should().Be(0.00m);
        quote.Discount.Should().Be(quote.Subtotal);
        quote.AgentRunId.Should().Be(runId);
    }

    [Fact]
    public async Task A_failed_run_moves_the_request_to_AgentFailed_with_the_agents_reason()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        var failed = AgentFixtures.View(AgentFixtures.Failed);
        env.AgentReturns(failed);

        await env.Poller.PollOnceAsync();

        var run = await env.RunAsync(runId);
        run.Status.Should().Be(AgentRunStatuses.Failed);
        run.FailureReason.Should().Be(failed.Error).And.StartWith("policy unavailable");
        run.CompletedAt.Should().NotBeNull();
        run.DurationMs.Should().NotBeNull();
        run.Nodes.Should().Equal(failed.Nodes);
        (await env.QueryAsync(db => db.AgentSteps.CountAsync(s => s.RunId == runId))).Should().Be(failed.Steps!.Count);
        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.AgentFailed);
        request.StatusHistory.OrderBy(h => h.Id).Last().Reason.Should().Be(failed.Error);
        (await env.QueryAsync(db => db.Quotations.AnyAsync(q => q.RequestId == requestId))).Should().BeFalse();
    }

    [Fact]
    public async Task A_run_the_agent_service_does_not_know_fails()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        env.Agent.Get = (_, _) => Task.FromResult(FakeAgentClient.NotFound<AgentWorkflowView>());

        await env.Poller.PollOnceAsync();

        (await env.RunAsync(runId)).FailureReason.Should().Be(AgentRunSync.NotFoundMessage);
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.AgentFailed);
    }

    [Theory]
    [InlineData(AgentWorkflowStatuses.Completed)]
    [InlineData(AgentWorkflowStatuses.Rejected)]
    [InlineData(AgentWorkflowStatuses.Cancelled)]
    public async Task An_ending_that_only_an_officer_decision_can_cause_fails_the_run(string status)
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        env.AgentReturns(Awaiting with { Status = status });

        await env.Poller.PollOnceAsync();

        (await env.RunAsync(runId)).FailureReason.Should().Be(AgentRunSync.UnexpectedStatusMessage(status));
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.AgentFailed);
    }

    [Fact]
    public async Task An_unavailable_agent_service_changes_nothing()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        env.Agent.Get = (_, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowView>("HTTP 503"));

        await env.Poller.PollOnceAsync();

        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Running);
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.AgentProcessing);
        (await env.QueryAsync(db => db.AgentSteps.CountAsync(s => s.RunId == runId))).Should().Be(0);
    }

    [Fact]
    public async Task Repeated_ticks_copy_each_trace_row_once()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        // While running, the view already carries some of the trace; the next tick sees the same rows plus more.
        var partial = Awaiting with
        {
            Status = AgentWorkflowStatuses.Running, Steps = Awaiting.Steps!.Take(3).ToList(), Validation = [], Proposal = null,
        };
        env.AgentReturns(partial);

        await env.Poller.PollOnceAsync();
        await env.Poller.PollOnceAsync();
        (await env.QueryAsync(db => db.AgentSteps.CountAsync(s => s.RunId == runId))).Should().Be(3);
        (await env.RunAsync(runId)).Nodes.Should().Equal(Awaiting.Nodes);

        env.AgentReturns(Awaiting);
        await env.Poller.PollOnceAsync();
        await env.Poller.PollOnceAsync(); // AwaitingApproval is no longer polled: nothing happens

        (await env.QueryAsync(db => db.AgentSteps.CountAsync(s => s.RunId == runId))).Should().Be(Awaiting.Steps!.Count);
        (await env.QueryAsync(db => db.AgentToolCalls.CountAsync(c => c.Step.RunId == runId)))
            .Should().Be(Awaiting.Steps!.Sum(s => s.ToolCalls!.Count));
        (await env.QueryAsync(db => db.ValidationResults.CountAsync(v => v.RunId == runId))).Should().Be(12);
        (await env.QueryAsync(db => db.Quotations.CountAsync(q => q.RequestId == requestId))).Should().Be(1);
        env.Agent.Calls.Count(c => c.Call == "get").Should().Be(3);
    }

    [Fact]
    public async Task One_run_that_throws_does_not_stop_the_others()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (badRequest, badRun) = await env.SubmitAsync(weekdaysAhead: 20);
        var (goodRequest, _) = await env.SubmitAsync(weekdaysAhead: 21);
        env.Agent.Get = (thread, _) => thread == badRun
            ? throw new InvalidOperationException("boom")
            : Task.FromResult(FakeAgentClient.Ok(Awaiting));

        await env.Poller.PollOnceAsync();

        (await env.RequestAsync(badRequest)).Status.Should().Be(RequestStatuses.AgentProcessing);
        (await env.RequestAsync(goodRequest)).Status.Should().Be(RequestStatuses.PendingApproval);
    }

    [Fact]
    public async Task A_request_moved_between_the_fetch_and_the_lock_is_skipped()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        env.Agent.Get = async (_, _) =>
        {
            // Another writer moves the request while the poller waits for the agent service.
            await MoveAsync(env.Factory, requestId, RequestStatuses.PendingApproval, RequestStatuses.Cancelled);
            return FakeAgentClient.Ok(Awaiting);
        };

        await env.Poller.PollOnceAsync();

        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.Cancelled);
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Running);
        (await env.QueryAsync(db => db.AgentSteps.CountAsync(s => s.RunId == runId))).Should().Be(0);
        (await env.QueryAsync(db => db.Quotations.AnyAsync(q => q.RequestId == requestId))).Should().BeFalse();
    }

    [Fact]
    public async Task A_proposal_that_cannot_be_priced_fails_the_run_with_the_reason()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        var json = AgentFixtures.Json(AgentFixtures.AwaitingStudent).Replace("\"MIC-WIRELESS\"", "\"NOPE-1\"");
        env.AgentReturns(System.Text.Json.JsonSerializer.Deserialize<AgentWorkflowView>(json, AgentJson.Options)!);

        await env.Poller.PollOnceAsync();

        (await env.RunAsync(runId)).FailureReason.Should().Be("The proposal names unknown equipment types: NOPE-1");
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.AgentFailed);
    }

    [Fact]
    public async Task Long_agent_text_is_truncated_to_the_column_limits()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (_, runId) = await env.SubmitAsync();
        var step = Awaiting.Steps![0] with { Sequence = 1, AgentName = new string('a', 80) };
        env.AgentReturns(Awaiting with
        {
            Status = AgentWorkflowStatuses.Failed, Error = new string('e', 3000), Model = new string('m', 300), Steps = [step],
        });

        await env.Poller.PollOnceAsync();

        var run = await env.RunAsync(runId);
        run.FailureReason.Should().HaveLength(AgentRunSync.FailureReasonMaxLength);
        run.Model.Should().HaveLength(AgentRunConfiguration.ModelMaxLength);
        (await env.QueryAsync(db => db.AgentSteps.Where(s => s.RunId == runId).Select(s => s.AgentName).SingleAsync()))
            .Should().HaveLength(AgentStepConfiguration.AgentNameMaxLength);
    }
}
