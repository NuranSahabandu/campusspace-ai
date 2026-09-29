using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The officer's decisions (UC19–UC21) and the approval transaction, over the fake agent service whose views are verbatim
/// agent-service responses (Fixtures/). Each test has its own seeded database (AgentPollerEnv).
/// </summary>
[Collection(PostgresCollection.Name)]
public class ApprovalDecisionTests(PostgresFixture fixture)
{
    // ---------- approve ----------

    [Fact]
    public async Task Approve_books_the_room_reserves_the_mics_issues_the_5500_quote_and_completes_the_run()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var draftId = await env.QueryAsync(db => db.Quotations.Where(q => q.RequestId == requestId).Select(q => q.Id).SingleAsync());
        var (_, officerId) = await env.OfficerAsync();

        var response = await env.DecideAsync(requestId, "approve", new { comment = "Looks good" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadJsonAsync()).GetProperty("status").GetString().Should().Be(RequestStatuses.Approved);

        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.Approved);
        var last = request.StatusHistory.OrderBy(h => h.Id).Last();
        (last.FromStatus, last.ToStatus, last.ChangedById, last.Reason)
            .Should().Be((RequestStatuses.PendingApproval, RequestStatuses.Approved, (long?)officerId, (string?)null));

        var booking = await env.QueryAsync(db => db.Bookings.AsNoTracking().SingleAsync(b => b.RequestId == requestId));
        var a301 = await RoomIdAsync(env, "A301");
        booking.RoomId.Should().Be(a301);
        booking.Status.Should().Be(BookingStatuses.Confirmed);
        booking.TimeRange.LowerBound.Should().Be(request.RequestedStart);
        booking.TimeRange.UpperBound.Should().Be(request.RequestedEnd);

        var reservations = await env.QueryAsync(db => db.EquipmentReservations.AsNoTracking()
            .Where(r => r.BookingId == booking.Id).Select(r => new { r.Type.Code, r.Quantity }).ToListAsync());
        // The projector is covered by A301 (room_builtin): nothing is reserved for it.
        reservations.Should().BeEquivalentTo([new { Code = "MIC-WIRELESS", Quantity = 2 }]);

        var quote = await env.QueryAsync(db => db.Quotations.AsNoTracking().SingleAsync(q => q.RequestId == requestId));
        (quote.Id, quote.Status, quote.Total, quote.IsExempt, quote.AgentRunId)
            .Should().Be((draftId, QuotationStatuses.Issued, 5500.00m, false, (Guid?)runId), "the matching Draft is the one issued");

        var decision = await env.QueryAsync(db => db.ApprovalDecisions.AsNoTracking().SingleAsync(d => d.AgentRunId == runId));
        (decision.RequestId, decision.OfficerId, decision.Decision, decision.Comment)
            .Should().Be((requestId, officerId, ApprovalDecisions.Approve, "Looks good"));

        var run = await env.RunAsync(runId);
        run.Status.Should().Be(AgentRunStatuses.Completed);
        run.CompletedAt.Should().NotBeNull();
        run.Nodes.Should().EndWith("finalize");
        (await env.QueryAsync(db => db.AgentSteps.AnyAsync(s => s.RunId == runId && s.AgentName == "finalize"))).Should().BeTrue();
        env.Agent.Calls.Should().Contain(("resume", runId, AgentDecisions.Approve));
    }

    [Fact]
    public async Task Approving_a_lecturers_request_issues_the_exempt_zero_quote()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync(lecturer: true);

        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);

        var quote = await env.QueryAsync(db => db.Quotations.AsNoTracking().SingleAsync(q => q.RequestId == requestId));
        (quote.Status, quote.Total, quote.IsExempt, quote.AgentRunId).Should().Be((QuotationStatuses.Issued, 0.00m, true, (Guid?)runId));
        quote.Subtotal.Should().BePositive("an exempt quote keeps its lines priced");
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.Approved);
    }

    [Fact]
    public async Task A_slow_agent_answers_202_ApprovalInProgress_with_the_decision_saved()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        env.Agent.Get = (thread, _) => Task.FromResult(FakeAgentClient.Ok(FakeAgentClient.RunningView(thread)));

        var response = await env.DecideAsync(requestId, "approve");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.ToString().Should().EndWith($"{Url}/{requestId}");
        var body = await response.ReadJsonAsync();
        (body.GetProperty("requestId").GetInt64(), body.GetProperty("status").GetString())
            .Should().Be((requestId, "ApprovalInProgress"));
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Resuming);
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.PendingApproval);
        (await env.QueryAsync(db => db.ApprovalDecisions.CountAsync(d => d.AgentRunId == runId))).Should().Be(1);
        (await env.QueryAsync(db => db.Bookings.AnyAsync(b => b.RequestId == requestId))).Should().BeFalse();
    }

    [Fact]
    public async Task A_lost_resume_answers_202_at_once()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        env.Agent.Resume = (_, _, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());

        var readsBefore = env.Agent.Calls.Count(c => c == ("get", runId, null));

        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.Resuming);
        env.Agent.Calls.Count(c => c == ("get", runId, null)).Should().Be(readsBefore, "approve doesn't wait for a resume that never arrived");
    }

    [Fact]
    public async Task Lead_time_met_at_submission_but_not_at_approval_still_approves()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync(weekdaysAhead: 3);
        var start = (await env.RequestAsync(requestId)).RequestedStart;
        // The officer approves 24 h before the start, inside the 48 h lead time: the requester submitted in time.
        env.Clock.Set(new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).AddHours(-24));

        var response = await env.DecideAsync(requestId, "approve");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.Approved);
    }

    // ---------- time failures: the request is closed ----------

    [Fact]
    public async Task A_start_in_the_past_at_approval_closes_the_request()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var start = (await env.RequestAsync(requestId)).RequestedStart;
        env.Clock.Set(new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).AddHours(1));

        var response = await env.DecideAsync(requestId, "approve");

        await ShouldBeClosedForTimeAsync(env, response, requestId, runId, "Start must be in the future");
    }

    [Fact]
    public async Task Closing_the_weekday_in_opening_hours_closes_the_request()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var start = new DateTimeOffset(DateTime.SpecifyKind((await env.RequestAsync(requestId)).RequestedStart, DateTimeKind.Utc));
        var day = CampusTime.DateOf(start).DayOfWeek;
        var hours = JsonNode.Parse(await env.QueryAsync(db => db.PolicySettings
            .Where(p => p.Key == PolicyKeys.OpeningHours).Select(p => p.Value).SingleAsync()))!.AsObject();
        hours[day.ToString()[..3].ToLowerInvariant()] = null;
        await env.SetPolicyAsync(PolicyKeys.OpeningHours, hours.ToJsonString());

        var response = await env.DecideAsync(requestId, "approve");

        await ShouldBeClosedForTimeAsync(env, response, requestId, runId, $"The campus is closed on {day}s");
    }

    [Fact]
    public async Task A_lead_time_raised_beyond_the_submission_margin_closes_the_request()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        // 20 weekdays ahead is under 2000 hours: the request would have failed at submission under this policy.
        await env.SetPolicyAsync(PolicyKeys.MinLeadTimeHours, "2000");

        var response = await env.DecideAsync(requestId, "approve");

        await ShouldBeClosedForTimeAsync(env, response, requestId, runId, "Must start at least 2000 hours from now (as of submission)");
    }

    // ---------- proposal failures: a new proposal ----------

    [Fact]
    public async Task A_failed_agent_finalize_prepares_a_new_proposal()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        env.AgentReturns(AgentFixtures.View(AgentFixtures.FinalizeFailed));

        var response = await env.DecideAsync(requestId, "approve");

        await ShouldPrepareNewProposalAsync(env, response, requestId, runId,
            "Final re-check failed: V02: Room A301 is no longer free for the requested window");
    }

    [Fact]
    public async Task Too_few_microphones_left_prepares_a_new_proposal()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        // Seven of the eight wireless mics go for repair after the proposal.
        await env.QueryAsync(async db =>
        {
            var keep = await db.EquipmentItems.Where(i => i.Type.Code == "MIC-WIRELESS").MinAsync(i => i.Id);
            return await db.EquipmentItems.Where(i => i.Type.Code == "MIC-WIRELESS" && i.Id != keep)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, EquipmentItemStatuses.UnderRepair));
        });

        var response = await env.DecideAsync(requestId, "approve");

        await ShouldPrepareNewProposalAsync(env, response, requestId, runId,
            "Not enough equipment: MIC-WIRELESS (requested 2, available 1)");
    }

    [Fact]
    public async Task A_blackout_added_after_the_proposal_prepares_a_new_proposal()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var (_, officerId) = await env.OfficerAsync();
        var request = await env.RequestAsync(requestId);
        var roomId = await RoomIdAsync(env, "A301");
        await env.QueryAsync(async db =>
        {
            db.RoomBlackouts.Add(new RoomBlackout
            {
                RoomId = roomId, Reason = "AC maintenance", CreatedById = officerId,
                TimeRange = CampusTime.UtcRange(Utc(request.RequestedStart), Utc(request.RequestedStart).AddHours(1)),
            });
            return await db.SaveChangesAsync();
        });

        var response = await env.DecideAsync(requestId, "approve");

        await ShouldPrepareNewProposalAsync(env, response, requestId, runId, "Room A301 is blacked out for this time");
    }

    [Fact]
    public async Task A_room_that_lost_the_feature_covering_a_builtin_line_prepares_a_new_proposal()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        await env.QueryAsync(db => db.RoomFeatures
            .Where(rf => rf.Room.Code == "A301" && rf.Feature.Code == "projector").ExecuteDeleteAsync());

        var response = await env.DecideAsync(requestId, "approve");

        await ShouldPrepareNewProposalAsync(env, response, requestId, runId,
            "Room A301 no longer has the projector feature that covers PROJ-PORTABLE");
    }

    [Fact]
    public async Task Two_approvals_of_the_same_room_and_time_book_it_once_and_the_loser_gets_a_new_proposal()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var first = await env.ToPendingApprovalAsync();
        var second = await env.ToPendingApprovalAsync();
        await env.OfficerAsync();

        var responses = await Task.WhenAll(env.DecideAsync(first.RequestId, "approve"), env.DecideAsync(second.RequestId, "approve"));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var (winner, loser) = responses[0].StatusCode == HttpStatusCode.OK ? (first, second) : (second, first);
        var lost = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        await ShouldPrepareNewProposalAsync(env, lost, loser.RequestId, loser.RunId, "Room A301 was just booked for this time");
        (await env.RequestAsync(winner.RequestId)).Status.Should().Be(RequestStatuses.Approved);
        (await env.QueryAsync(db => db.Bookings.CountAsync())).Should().Be(1);
    }

    // ---------- reject ----------

    [Fact]
    public async Task Reject_closes_the_request_ends_the_run_voids_the_draft_and_tells_the_agent()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var (_, officerId) = await env.OfficerAsync();

        var response = await env.DecideAsync(requestId, "reject", new { reason = "  The hall is reserved for exams.  " });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadJsonAsync()).GetProperty("status").GetString().Should().Be(RequestStatuses.Rejected);
        var last = (await env.RequestAsync(requestId)).StatusHistory.OrderBy(h => h.Id).Last();
        (last.FromStatus, last.ToStatus, last.ChangedById, last.Reason).Should().Be(
            (RequestStatuses.PendingApproval, RequestStatuses.Rejected, (long?)officerId, "The hall is reserved for exams."));
        var run = await env.RunAsync(runId);
        run.Status.Should().Be(AgentRunStatuses.Rejected);
        run.CompletedAt.Should().NotBeNull();
        (await QuoteStatusesAsync(env, requestId)).Should().Equal(QuotationStatuses.Void);
        var decision = await env.QueryAsync(db => db.ApprovalDecisions.AsNoTracking().SingleAsync(d => d.AgentRunId == runId));
        (decision.Decision, decision.Comment).Should().Be((ApprovalDecisions.Reject, "The hall is reserved for exams."));
        env.Agent.Calls.Should().Contain(("resume", runId, AgentDecisions.Reject));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Reject_needs_a_reason(string? reason)
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();

        var problem = await (await env.DecideAsync(requestId, "reject", new { reason })).ShouldBeProblemAsync(400);

        problem.GetProperty("errors").TryGetProperty("Reason", out _).Should().BeTrue();
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.AwaitingApproval);
    }

    // ---------- request-revision ----------

    [Fact]
    public async Task Request_revision_resumes_the_same_run_as_revision_2_and_sends_the_notes()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var (_, officerId) = await env.OfficerAsync();
        string? sentNotes = null;
        env.Agent.Resume = (thread, _, notes, _) =>
        {
            sentNotes = notes;
            return Task.FromResult(FakeAgentClient.Accepted(thread));
        };
        const string Notes = "Use a lab in the New Building; A301 has AC maintenance.";

        var response = await env.DecideAsync(requestId, "request-revision", new { notes = Notes });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.ToString().Should().EndWith($"{Url}/{requestId}");
        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.AgentProcessing);
        request.StatusHistory.OrderBy(h => h.Id).TakeLast(2).Select(h => (h.FromStatus, h.ToStatus, h.ChangedById, h.Reason))
            .Should().Equal(
                (RequestStatuses.PendingApproval, RequestStatuses.RevisionRequested, officerId, Notes),
                (RequestStatuses.RevisionRequested, RequestStatuses.AgentProcessing, officerId, Notes));
        var run = await env.RunAsync(runId);
        (run.Status, run.RevisionNo).Should().Be((AgentRunStatuses.Resuming, 2));
        (await env.QueryAsync(db => db.AgentRuns.CountAsync(r => r.RequestId == requestId))).Should().Be(1, "the same run continues");
        (await QuoteStatusesAsync(env, requestId)).Should().Equal(QuotationStatuses.Void);
        (await env.QueryAsync(db => db.ApprovalDecisions.SingleAsync(d => d.AgentRunId == runId))).Decision.Should().Be(ApprovalDecisions.Revise);
        env.Agent.Calls.Should().Contain(("resume", runId, AgentDecisions.Revise));
        sentNotes.Should().Be(Notes);
    }

    [Fact]
    public async Task Request_revision_needs_notes()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();

        var problem = await (await env.DecideAsync(requestId, "request-revision")).ShouldBeProblemAsync(400);

        problem.GetProperty("errors").TryGetProperty("Notes", out _).Should().BeTrue();
    }

    // ---------- guards ----------

    [Fact]
    public async Task Deciding_a_request_that_is_not_pending_approval_is_409()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.SubmitAsync();

        foreach (var (action, body) in new (string, object)[]
                 { ("approve", new { }), ("reject", new { reason = "No" }), ("request-revision", new { notes = "Other room" }) })
        {
            var problem = await (await env.DecideAsync(requestId, action, body)).ShouldBeProblemAsync(409);
            problem.GetProperty("title").GetString().Should().Be(ApprovalService.NotPendingMessage(RequestStatuses.AgentProcessing));
        }
    }

    [Fact]
    public async Task A_second_decision_while_the_first_is_being_finished_is_409_and_cancel_waits_too()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        env.Agent.Get = (thread, _) => Task.FromResult(FakeAgentClient.Ok(FakeAgentClient.RunningView(thread)));
        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        foreach (var (action, body) in new (string, object)[]
                 { ("approve", new { }), ("reject", new { reason = "No" }), ("request-revision", new { notes = "Other room" }) })
            (await (await env.DecideAsync(requestId, action, body)).ShouldBeProblemAsync(409))
                .GetProperty("title").GetString().Should().Be(ApprovalService.AlreadyDecidingMessage);

        var cancel = await (await env.DecideAsync(requestId, "cancel", new { reason = "Event called off" })).ShouldBeProblemAsync(409);
        cancel.GetProperty("title").GetString().Should().Be(BookingRequestService.ApprovalInProgressMessage);
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.PendingApproval);
    }

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Lecturer)]
    [InlineData(Roles.LabTechnician)]
    public async Task Only_a_facilities_officer_can_decide(string role)
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var client = TestAuth.CreateClient(env.Factory, role);

        (await client.PostAsJsonAsync($"{Url}/{requestId}/approve", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"{Url}/{requestId}/reject", new { reason = "No" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"{Url}/{requestId}/request-revision", new { notes = "Other" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await env.RunAsync(runId)).Status.Should().Be(AgentRunStatuses.AwaitingApproval);
    }

    [Fact]
    public async Task Deciding_an_unknown_request_is_404()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);

        (await env.DecideAsync(999_999, "approve")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await env.DecideAsync(999_999, "reject", new { reason = "No" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await env.DecideAsync(999_999, "request-revision", new { notes = "Other" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- helpers ----------

    /// <summary>The time can't be fixed by a re-plan: run Failed, request Rejected by the system, Draft Void, no new run.</summary>
    internal static async Task ShouldBeClosedForTimeAsync(
        AgentPollerEnv env, HttpResponseMessage? response, long requestId, Guid runId, string reason)
    {
        if (response is not null)
            (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString()
                .Should().Be(ApprovalFinalizer.TimeClosedMessage(reason));
        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.Rejected);
        var last = request.StatusHistory.OrderBy(h => h.Id).Last();
        (last.FromStatus, last.ToStatus, last.ChangedById, last.Reason).Should().Be(
            (RequestStatuses.PendingApproval, RequestStatuses.Rejected, (long?)null, ApprovalFinalizer.TimeClosedReason(reason)));
        var run = await env.RunAsync(runId);
        (run.Status, run.FailureReason).Should().Be((AgentRunStatuses.Failed, reason));
        run.PolicySnapshotJson.Should().NotBeNullOrEmpty();
        (await env.QueryAsync(db => db.AgentRuns.CountAsync(r => r.RequestId == requestId))).Should().Be(1, "no new run");
        (await QuoteStatusesAsync(env, requestId)).Should().Equal(QuotationStatuses.Void);
        (await env.QueryAsync(db => db.Bookings.AnyAsync(b => b.RequestId == requestId))).Should().BeFalse();
        env.Agent.Calls.Should().Contain(("resume", runId, AgentDecisions.Cancel));
    }

    /// <summary>The proposal is stale: run Failed, request RevisionRequested → AgentProcessing, a new run (RevisionNo 2).</summary>
    internal static async Task ShouldPrepareNewProposalAsync(
        AgentPollerEnv env, HttpResponseMessage? response, long requestId, Guid runId, string reason)
    {
        if (response is not null)
            (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString()
                .Should().Be(ApprovalFinalizer.NewProposalMessage(reason));
        var request = await env.RequestAsync(requestId);
        request.Status.Should().Be(RequestStatuses.AgentProcessing);
        request.StatusHistory.OrderBy(h => h.Id).TakeLast(2).Select(h => (h.FromStatus, h.ToStatus, h.ChangedById, h.Reason))
            .Should().Equal(
                (RequestStatuses.PendingApproval, RequestStatuses.RevisionRequested, null, reason),
                (RequestStatuses.RevisionRequested, RequestStatuses.AgentProcessing, null, null));
        var run = await env.RunAsync(runId);
        (run.Status, run.FailureReason).Should().Be((AgentRunStatuses.Failed, reason));
        var next = await env.QueryAsync(db => db.AgentRuns.AsNoTracking().SingleAsync(r => r.RequestId == requestId && r.Id != runId));
        (next.RevisionNo, next.Status).Should().Be((2, AgentRunStatuses.Running));
        env.Agent.Calls.Should().Contain(("start", next.Id, null));
        (await QuoteStatusesAsync(env, requestId)).Should().Equal(QuotationStatuses.Void);
        (await env.QueryAsync(db => db.Bookings.AnyAsync(b => b.RequestId == requestId))).Should().BeFalse();
    }

    private static Task<List<string>> QuoteStatusesAsync(AgentPollerEnv env, long requestId) =>
        env.QueryAsync(db => db.Quotations.Where(q => q.RequestId == requestId).OrderBy(q => q.Id).Select(q => q.Status).ToListAsync());

    private static Task<long> RoomIdAsync(AgentPollerEnv env, string code) =>
        env.QueryAsync(db => db.Rooms.Where(r => r.Code == code).Select(r => r.Id).SingleAsync());

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
