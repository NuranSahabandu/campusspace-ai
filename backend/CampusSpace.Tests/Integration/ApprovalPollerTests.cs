using System.Net;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static CampusSpace.Tests.Integration.ApprovalDecisionTests;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The poller's Resuming path: finishing an approval the approve call didn't wait for, returning a revision to
/// PendingApproval, re-sending a lost resume, the Resuming watchdog, and orphaned runs.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ApprovalPollerTests(PostgresFixture fixture)
{
    private const string Notes = "Use a lab in the New Building; A301 has AC maintenance.";

    /// <summary>Approve with a slow agent: 202, the decision saved, the run Resuming.</summary>
    private static async Task<(long RequestId, Guid RunId)> ApprovedSlowlyAsync(AgentPollerEnv env, object? body = null)
    {
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        env.Agent.Get = (thread, _) => Task.FromResult(FakeAgentClient.Ok(FakeAgentClient.RunningView(thread)));
        (await env.DecideAsync(requestId, "approve", body)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (requestId, runId);
    }

    private static async Task<(long RequestId, Guid RunId)> RevisedAsync(AgentPollerEnv env)
    {
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "request-revision", new { notes = Notes })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (requestId, runId);
    }

    // ---------- approve ----------

    [Fact]
    public async Task The_poller_finishes_a_slow_approval_once_however_many_ticks_run()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await ApprovedSlowlyAsync(env);
        env.AgentReturns(AgentFixtures.View(AgentFixtures.CompletedStudent));

        await env.Poller.PollOnceAsync();
        await env.Poller.PollOnceAsync();
        await Task.WhenAll(env.Poller.PollOnceAsync(), env.Poller.PollOnceAsync());

        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.Approved);
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Completed);
        (await env.QueryAsync(db => db.Bookings.CountAsync(b => b.RequestId == requestId))).Should().Be(1);
        (await env.QueryAsync(db => db.EquipmentReservations.Where(r => r.Booking.RequestId == requestId).SumAsync(r => r.Quantity)))
            .Should().Be(2);
        var quote = await env.QueryAsync(db => db.Quotations.AsNoTracking().SingleAsync(q => q.RequestId == requestId));
        (quote.Status, quote.Total, quote.AgentRunId).Should().Be((QuotationStatuses.Issued, 5500.00m, (Guid?)runId));
        var last = (await env.RequestAsync(requestId)).StatusHistory.OrderBy(h => h.Id).Last();
        last.ChangedById.Should().Be((await env.OfficerAsync()).UserId, "the officer who decided is the actor, even from the poller");
    }

    [Fact]
    public async Task A_lost_approve_is_re_sent_from_the_saved_decision()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        env.Agent.Resume = (_, _, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());
        (await env.DecideAsync(requestId, "approve", new { comment = "Fine by me" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        // The agent is still paused on the proposal the officer saw (same validation attempt).
        env.AgentReturns(AgentFixtures.View(AgentFixtures.AwaitingStudent));
        var sent = new List<(string Decision, string? Notes)>();
        env.Agent.Resume = (thread, decision, notes, _) =>
        {
            sent.Add((decision, notes));
            return Task.FromResult(FakeAgentClient.Accepted(thread));
        };

        await env.Poller.PollOnceAsync();

        sent.Should().Equal((AgentDecisions.Approve, "Fine by me"));
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Resuming);
    }

    [Fact]
    public async Task A_failed_finalize_seen_by_the_poller_prepares_a_new_proposal()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await ApprovedSlowlyAsync(env);
        env.AgentReturns(AgentFixtures.View(AgentFixtures.FinalizeFailed));

        await env.Poller.PollOnceAsync();

        await ShouldPrepareNewProposalAsync(env, null, requestId, runId,
            "Final re-check failed: V02: Room A301 is no longer free for the requested window");
    }

    [Fact]
    public async Task A_time_failure_seen_by_the_poller_closes_the_request()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await ApprovedSlowlyAsync(env);
        var start = (await env.RequestAsync(requestId)).RequestedStart;
        env.Clock.Set(new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).AddMinutes(30));
        env.AgentReturns(AgentFixtures.View(AgentFixtures.CompletedStudent));

        await env.Poller.PollOnceAsync();

        await ShouldBeClosedForTimeAsync(env, null, requestId, runId, "Start must be in the future");
    }

    [Fact]
    public async Task An_approval_the_agent_never_confirms_prepares_a_new_proposal_after_the_run_timeout()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await ApprovedSlowlyAsync(env);

        await env.Poller.PollOnceAsync();
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Resuming, "still inside the timeout");

        env.Clock.Advance(TimeSpan.FromMinutes(5));
        await env.Poller.PollOnceAsync();

        await ShouldPrepareNewProposalAsync(env, null, requestId, runId, AgentRunSync.ApprovalNotConfirmedMessage);
    }

    [Fact]
    public async Task An_unexpected_status_after_an_approve_prepares_a_new_proposal()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await ApprovedSlowlyAsync(env);
        env.AgentReturns(AgentFixtures.View(AgentFixtures.CompletedStudent) with { Status = AgentWorkflowStatuses.Cancelled });

        await env.Poller.PollOnceAsync();

        await ShouldPrepareNewProposalAsync(env, null, requestId, runId, AgentRunSync.UnexpectedResumingMessage("cancelled"));
    }

    // ---------- revise ----------

    [Fact]
    public async Task The_revised_proposal_returns_the_request_to_PendingApproval_with_a_new_draft_and_attempt_2()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await RevisedAsync(env);
        var oldDraft = await env.QueryAsync(db => db.Quotations.Where(q => q.RequestId == requestId).Select(q => q.Id).SingleAsync());
        var revised = AgentFixtures.View(AgentFixtures.RevisedStudent);
        env.AgentReturns(revised);

        await env.Poller.PollOnceAsync();
        await env.Poller.PollOnceAsync(); // AwaitingApproval is not polled: nothing changes or doubles

        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.PendingApproval);
        var last = request.StatusHistory.OrderBy(h => h.Id).Last();
        (last.FromStatus, last.ToStatus).Should().Be((RequestStatuses.AgentProcessing, RequestStatuses.PendingApproval));
        var run = await env.RunAsync(runId);
        (run.Status, run.RevisionNo).Should().Be((AgentRunStatuses.AwaitingApproval, 2));
        run.ProposalJson.Should().Contain("N201");
        run.PolicySnapshotJson.Should().NotBeNullOrEmpty();
        run.Nodes.Should().Equal(revised.Nodes);

        (await env.QueryAsync(db => db.ValidationResults.CountAsync(v => v.RunId == runId && v.Attempt == 2))).Should().Be(12);
        (await env.QueryAsync(db => db.ValidationResults.CountAsync(v => v.RunId == runId))).Should().Be(revised.Validation!.Count);
        (await env.QueryAsync(db => db.AgentSteps.CountAsync(s => s.RunId == runId))).Should().Be(revised.Steps!.Count);

        var quotes = await env.QueryAsync(db => db.Quotations.AsNoTracking().Where(q => q.RequestId == requestId).OrderBy(q => q.Id).ToListAsync());
        quotes.Select(q => (q.Id == oldDraft, q.Status)).Should().Equal((true, QuotationStatuses.Void), (false, QuotationStatuses.Draft));
        quotes[1].AgentRunId.Should().Be(runId);
    }

    [Fact]
    public async Task A_lost_revise_is_re_sent_with_the_saved_notes()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        env.Agent.Resume = (_, _, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());
        (await env.DecideAsync(requestId, "request-revision", new { notes = Notes })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        env.AgentReturns(AgentFixtures.View(AgentFixtures.AwaitingStudent));
        var sent = new List<(string Decision, string? Notes)>();
        env.Agent.Resume = (thread, decision, notes, _) =>
        {
            sent.Add((decision, notes));
            return Task.FromResult(FakeAgentClient.Accepted(thread));
        };

        await env.Poller.PollOnceAsync();

        sent.Should().Equal((AgentDecisions.Revise, Notes));
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.AgentProcessing);
    }

    [Fact]
    public async Task A_revision_that_fails_ends_the_run_and_the_request_is_AgentFailed()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await RevisedAsync(env);
        var failed = AgentFixtures.View(AgentFixtures.Failed);
        env.AgentReturns(failed);

        await env.Poller.PollOnceAsync();

        var run = await env.RunAsync(runId);
        (run.Status, run.FailureReason).Should().Be((AgentRunStatuses.Failed, failed.Error));
        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.AgentFailed);
        request.StatusHistory.OrderBy(h => h.Id).Last().Reason.Should().Be(failed.Error);
    }

    [Fact]
    public async Task A_revision_the_agent_never_delivers_times_out_to_AgentFailed()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await RevisedAsync(env);
        env.Agent.Get = (thread, _) => Task.FromResult(FakeAgentClient.Ok(FakeAgentClient.RunningView(thread)));

        env.Clock.Advance(TimeSpan.FromMinutes(5));
        await env.Poller.PollOnceAsync();

        (await env.RunAsync(runId)).FailureReason.Should().Be(AgentRunSync.TimedOutMessage);
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.AgentFailed);
    }

    [Fact]
    public async Task A_completed_run_after_a_revise_is_unexpected_and_fails_to_AgentFailed()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await RevisedAsync(env);
        env.AgentReturns(AgentFixtures.View(AgentFixtures.CompletedStudent));

        await env.Poller.PollOnceAsync();

        (await env.RunAsync(runId)).FailureReason.Should().Be(AgentRunSync.UnexpectedResumingMessage("completed"));
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.AgentFailed);
        (await env.QueryAsync(db => db.Bookings.AnyAsync(b => b.RequestId == requestId))).Should().BeFalse();
    }

    // ---------- orphans ----------

    [Fact]
    public async Task A_queued_run_whose_request_moved_on_is_failed_as_orphaned()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        env.Agent.Start = (_, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());
        var (requestId, runId) = await env.SubmitAsync();
        await SetRequestStatusAsync(env, requestId, RequestStatuses.AgentFailed);

        await env.Poller.PollOnceAsync();

        await ShouldBeOrphanedAsync(env, requestId, runId, RequestStatuses.AgentFailed, cancelSent: false);
    }

    [Fact]
    public async Task A_running_run_whose_request_is_pending_approval_is_failed_as_orphaned()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        await SetRequestStatusAsync(env, requestId, RequestStatuses.PendingApproval);

        await env.Poller.PollOnceAsync();

        await ShouldBeOrphanedAsync(env, requestId, runId, RequestStatuses.PendingApproval, cancelSent: true);
    }

    [Fact]
    public async Task A_resuming_approval_whose_request_is_not_pending_approval_is_failed_as_orphaned()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await ApprovedSlowlyAsync(env);
        await SetRequestStatusAsync(env, requestId, RequestStatuses.AgentProcessing);

        await env.Poller.PollOnceAsync();

        await ShouldBeOrphanedAsync(env, requestId, runId, RequestStatuses.AgentProcessing, cancelSent: true);
    }

    [Fact]
    public async Task A_resuming_revision_whose_request_is_pending_approval_is_failed_as_orphaned()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await RevisedAsync(env);
        await SetRequestStatusAsync(env, requestId, RequestStatuses.PendingApproval);

        await env.Poller.PollOnceAsync();

        await ShouldBeOrphanedAsync(env, requestId, runId, RequestStatuses.PendingApproval, cancelSent: true);
    }

    [Fact]
    public async Task A_resuming_run_without_a_decision_is_failed_as_orphaned()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.SubmitAsync();
        await env.QueryAsync(db => db.AgentRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, AgentRunStatuses.Resuming)));

        await env.Poller.PollOnceAsync();

        await ShouldBeOrphanedAsync(env, requestId, runId, RequestStatuses.AgentProcessing, cancelSent: true);
    }

    /// <summary>Simulates a request moved by hand (SQL, legacy data), bypassing the state machine.</summary>
    private static Task<int> SetRequestStatusAsync(AgentPollerEnv env, long requestId, string status) =>
        env.QueryAsync(db => db.BookingRequests.Where(r => r.Id == requestId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status)));

    private static async Task ShouldBeOrphanedAsync(AgentPollerEnv env, long requestId, Guid runId, string requestStatus, bool cancelSent)
    {
        var run = await env.RunAsync(runId);
        (run.Status, run.FailureReason).Should().Be((AgentRunStatuses.Failed, AgentRunSync.OrphanedMessage(requestStatus)));
        run.CompletedAt.Should().NotBeNull();
        (await env.RequestAsync(requestId)).Status.Should().Be(requestStatus, "an orphan never touches its request");
        env.Agent.Calls.Contains(("resume", runId, AgentDecisions.Cancel)).Should().Be(cancelSent);
        (await env.QueryAsync(db => db.AgentRuns.CountAsync(r => r.RequestId == requestId))).Should().Be(1);
    }
}
