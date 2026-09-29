using System.Net;
using System.Text.Json;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The officer's read endpoints (UC17, UC18) and the requester's LatestProposal summary, over the fake agent service with
/// verbatim agent-service fixtures. Each test has its own seeded database (AgentPollerEnv).
/// </summary>
[Collection(PostgresCollection.Name)]
public class ApprovalReadEndpointsTests(PostgresFixture fixture)
{
    private const string Queue = "/api/approvals/queue";
    private static string Runs(long requestId) => $"{Url}/{requestId}/agent-runs";
    private static string Run(Guid id) => $"/api/agent-runs/{id}";

    private static async Task<HttpClient> OwnerAsync(AgentPollerEnv env, long requestId)
    {
        var owner = await env.QueryAsync(db => db.BookingRequests.Where(r => r.Id == requestId)
            .Select(r => new { r.RequesterId, r.Requester.Role }).SingleAsync());
        return TestAuth.CreateClient(env.Factory, owner.Role, owner.RequesterId);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.ReadJsonAsync();
    }

    // ---------- authorization ----------

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Lecturer)]
    [InlineData(Roles.LabTechnician)]
    [InlineData(Roles.Admin)]
    public async Task Only_Facilities_Officers_read_the_queue_and_the_agent_runs(string role)
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var client = TestAuth.CreateClient(env.Factory, role);

        foreach (var url in new[] { Queue, Runs(requestId), Run(runId) })
            await (await client.GetAsync(url)).ShouldBeProblemAsync(403);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var client = env.Factory.CreateClient();

        foreach (var url in new[] { Queue, Runs(1), Run(Guid.NewGuid()) })
            (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_owner_sees_LatestProposal_but_not_the_agent_runs()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var owner = await OwnerAsync(env, requestId);
        var (officer, _) = await env.OfficerAsync();

        var ownerView = (await GetJsonAsync(owner, $"{Url}/{requestId}")).GetProperty("latestProposal");
        var officerView = (await GetJsonAsync(officer, $"{Url}/{requestId}")).GetProperty("latestProposal");

        ownerView.GetRawText().Should().Be(officerView.GetRawText(), "owner and officer get the same summary");
        await (await owner.GetAsync(Runs(requestId))).ShouldBeProblemAsync(403);
        await (await owner.GetAsync(Run(runId))).ShouldBeProblemAsync(403);
        ownerView.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["runId", "revisionNo", "roomId", "roomCode", "roomName", "quoteId", "total", "exempt", "quoteStatus"],
            "the requester's summary never carries the trace, plan or policy");
    }

    [Fact]
    public async Task Unknown_request_or_run_is_404()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (officer, _) = await env.OfficerAsync();

        await (await officer.GetAsync(Runs(999_999))).ShouldBeProblemAsync(404);
        await (await officer.GetAsync(Run(Guid.NewGuid()))).ShouldBeProblemAsync(404);
    }

    // ---------- queue ----------

    [Fact]
    public async Task Queue_lists_only_pending_requests_oldest_pending_first_with_room_quote_and_revision()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (first, _) = await env.ToPendingApprovalAsync(weekdaysAhead: 20);
        var (second, _) = await env.ToPendingApprovalAsync(lecturer: true, weekdaysAhead: 21);
        var (processing, _) = await env.SubmitAsync(weekdaysAhead: 22);
        var (officer, _) = await env.OfficerAsync();

        var page = await GetJsonAsync(officer, Queue);

        page.GetProperty("total").GetInt32().Should().Be(2);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("requestId").GetInt64()).Should().Equal(first, second);
        items.Select(i => i.GetProperty("requestId").GetInt64()).Should().NotContain(processing);

        var student = items[0];
        student.GetProperty("proposedRoomCode").GetString().Should().Be("A301");
        student.GetProperty("draftTotal").GetDecimal().Should().Be(5500.00m);
        student.GetProperty("exempt").GetBoolean().Should().BeFalse();
        student.GetProperty("revisionNo").GetInt32().Should().Be(1);
        student.GetProperty("requesterRole").GetString().Should().Be(Roles.Student);
        student.GetProperty("clubName").GetString().Should().NotBeNullOrEmpty();
        student.GetProperty("attendees").GetInt32().Should().BeGreaterThan(0);

        var lecturer = items[1];
        lecturer.GetProperty("exempt").GetBoolean().Should().BeTrue();
        lecturer.GetProperty("draftTotal").GetDecimal().Should().Be(0m);
        lecturer.GetProperty("clubName").ValueKind.Should().Be(JsonValueKind.Null);

        var descending = await GetJsonAsync(officer, $"{Queue}?sort=-pendingSince");
        descending.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("requestId").GetInt64())
            .Should().Equal(second, first);
    }

    [Fact]
    public async Task Queue_pages_searches_and_rejects_unknown_sort_fields()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (first, _) = await env.ToPendingApprovalAsync(weekdaysAhead: 20);
        var (second, _) = await env.ToPendingApprovalAsync(weekdaysAhead: 21);
        var (officer, _) = await env.OfficerAsync();

        var page2 = await GetJsonAsync(officer, $"{Queue}?page=2&pageSize=1");
        (page2.GetProperty("total").GetInt32(), page2.GetProperty("page").GetInt32()).Should().Be((2, 2));
        page2.GetProperty("items").EnumerateArray().Single().GetProperty("requestId").GetInt64().Should().Be(second);

        var email = await env.QueryAsync(db => db.BookingRequests.Where(r => r.Id == first).Select(r => r.Requester.Email).SingleAsync());
        var found = await GetJsonAsync(officer, $"{Queue}?search={Uri.EscapeDataString(email)}");
        found.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("requestId").GetInt64()).Should().Equal(first);

        await (await officer.GetAsync($"{Queue}?sort=purpose")).ShouldBeProblemAsync(400);
    }

    [Fact]
    public async Task Queue_is_empty_when_nothing_is_pending()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        await env.SubmitAsync();
        var (officer, _) = await env.OfficerAsync();

        var page = await GetJsonAsync(officer, Queue);

        page.GetProperty("total").GetInt32().Should().Be(0);
        page.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    // ---------- agent runs ----------

    [Fact]
    public async Task Run_detail_has_the_proposal_trace_grouped_validation_and_an_unchanged_policy()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var (officer, _) = await env.OfficerAsync();

        var runs = await GetJsonAsync(officer, Runs(requestId));
        runs.EnumerateArray().Select(r => (r.GetProperty("id").GetGuid(), r.GetProperty("status").GetString()))
            .Should().Equal((runId, AgentRunStatuses.AwaitingApproval));

        var run = await GetJsonAsync(officer, Run(runId));
        (run.GetProperty("requestId").GetInt64(), run.GetProperty("revisionNo").GetInt32(), run.GetProperty("status").GetString())
            .Should().Be((requestId, 1, AgentRunStatuses.AwaitingApproval));
        run.GetProperty("nodes").EnumerateArray().Select(n => n.GetString()).Should().EndWith("human_gate");
        run.GetProperty("officerSummary").GetString().Should().Contain("A301");

        var proposal = run.GetProperty("proposal");
        proposal.GetProperty("chosen").GetProperty("code").GetString().Should().Be("A301");
        proposal.GetProperty("chosen").GetProperty("reason").GetString().Should().Contain("45 attendees");
        proposal.GetProperty("alternatives").EnumerateArray().Select(a => a.GetProperty("code").GetString()).Should().Equal("N201");
        proposal.GetProperty("equipmentLines").EnumerateArray()
            .Select(l => (l.GetProperty("typeCode").GetString(), l.GetProperty("qty").GetInt32(), l.GetProperty("source").GetString()))
            .Should().Equal(("MIC-WIRELESS", 2, "portable"), ("PROJ-PORTABLE", 0, "room_builtin"));
        proposal.TryGetProperty("quote", out _).Should().BeFalse("prices come only from the .NET quotation");

        var steps = run.GetProperty("steps").EnumerateArray().ToList();
        steps.Select(s => s.GetProperty("sequence").GetInt32()).Should().BeInAscendingOrder();
        steps[0].GetProperty("agentName").GetString().Should().Be("supervisor");
        steps[0].GetProperty("output").ValueKind.Should().Be(JsonValueKind.Object, "jsonb is returned as JSON, not a string");
        var toolCall = steps[0].GetProperty("toolCalls").EnumerateArray().First();
        toolCall.GetProperty("toolName").GetString().Should().Be("get_policy");
        toolCall.GetProperty("args").ValueKind.Should().Be(JsonValueKind.Object);
        toolCall.GetProperty("succeeded").GetBoolean().Should().BeTrue();

        var validation = run.GetProperty("validation").EnumerateArray().ToList();
        validation.Should().ContainSingle();
        validation[0].GetProperty("attempt").GetInt32().Should().Be(1);
        var rules = validation[0].GetProperty("rules").EnumerateArray().ToList();
        rules.Select(r => r.GetProperty("rule").GetString()).Should().Equal(Enumerable.Range(1, 12).Select(i => $"V{i:00}"));
        rules.Should().OnlyContain(r => r.GetProperty("passed").GetBoolean());

        run.GetProperty("decisions").GetArrayLength().Should().Be(0);
        run.GetProperty("policySnapshot").GetProperty("min_lead_time_hours").GetInt32().Should().Be(48);
        run.GetProperty("policyChangedKeys").GetArrayLength().Should().Be(0, "the seeded policy matches the fixture's snapshot");
    }

    [Fact]
    public async Task PolicyChangedKeys_lists_a_key_changed_after_the_proposal()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (_, runId) = await env.ToPendingApprovalAsync();
        var (officer, _) = await env.OfficerAsync();

        await env.SetPolicyAsync(PolicyKeys.MinLeadTimeHours, "24");

        var run = await GetJsonAsync(officer, Run(runId));
        run.GetProperty("policyChangedKeys").EnumerateArray().Select(k => k.GetString()).Should().Equal(PolicyKeys.MinLeadTimeHours);
    }

    [Fact]
    public async Task A_revise_shows_the_decision_the_second_attempt_first_and_revision_2()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "request-revision", new { notes = "Use the New Building" })).StatusCode
            .Should().Be(HttpStatusCode.Accepted);
        env.AgentReturns(AgentFixtures.View(AgentFixtures.RevisedStudent));
        await env.Poller.PollOnceAsync();
        var (officer, _) = await env.OfficerAsync();

        var run = await GetJsonAsync(officer, Run(runId));

        run.GetProperty("revisionNo").GetInt32().Should().Be(2);
        run.GetProperty("proposal").GetProperty("chosen").GetProperty("code").GetString().Should().Be("N201");
        run.GetProperty("validation").EnumerateArray().Select(a => a.GetProperty("attempt").GetInt32()).Should().Equal(2, 1);
        var decision = run.GetProperty("decisions").EnumerateArray().Single();
        (decision.GetProperty("decision").GetString(), decision.GetProperty("comment").GetString())
            .Should().Be((ApprovalDecisions.Revise, "Use the New Building"));
        decision.GetProperty("officerName").GetString().Should().NotBeNullOrEmpty();
    }

    // ---------- LatestProposal ----------

    [Fact]
    public async Task LatestProposal_is_null_while_processing_and_the_Draft_once_pending()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (officer, _) = await env.OfficerAsync();
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        var (processing, _) = await env.SubmitAsync(weekdaysAhead: 22); // after the tick, so it stays AgentProcessing

        (await GetJsonAsync(officer, $"{Url}/{processing}")).GetProperty("latestProposal").ValueKind.Should().Be(JsonValueKind.Null);

        var detail = await GetJsonAsync(officer, $"{Url}/{requestId}");
        detail.GetProperty("requester").GetProperty("role").GetString().Should().Be(Roles.Student);
        var latest = detail.GetProperty("latestProposal");
        var a301 = await env.QueryAsync(db => db.Rooms.Where(r => r.Code == "A301").Select(r => new { r.Id, r.Name }).SingleAsync());
        latest.GetProperty("runId").GetGuid().Should().Be(runId);
        latest.GetProperty("revisionNo").GetInt32().Should().Be(1);
        (latest.GetProperty("roomId").GetInt64(), latest.GetProperty("roomCode").GetString(), latest.GetProperty("roomName").GetString())
            .Should().Be((a301.Id, "A301", a301.Name));
        (latest.GetProperty("total").GetDecimal(), latest.GetProperty("exempt").GetBoolean(), latest.GetProperty("quoteStatus").GetString())
            .Should().Be((5500.00m, false, QuotationStatuses.Draft));
    }

    [Fact]
    public async Task LatestProposal_after_approve_is_the_Issued_quote_and_the_booked_room()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        var owner = await OwnerAsync(env, requestId);

        var latest = (await GetJsonAsync(owner, $"{Url}/{requestId}")).GetProperty("latestProposal");

        (latest.GetProperty("roomCode").GetString(), latest.GetProperty("total").GetDecimal(), latest.GetProperty("quoteStatus").GetString())
            .Should().Be(("A301", 5500.00m, QuotationStatuses.Issued));
    }

    [Fact]
    public async Task LatestProposal_after_revise_is_null_then_the_new_Draft_with_revision_2()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        var owner = await OwnerAsync(env, requestId);
        (await env.DecideAsync(requestId, "request-revision", new { notes = "Use the New Building" })).StatusCode
            .Should().Be(HttpStatusCode.Accepted);

        (await GetJsonAsync(owner, $"{Url}/{requestId}")).GetProperty("latestProposal").ValueKind.Should().Be(JsonValueKind.Null);

        var revised = AgentFixtures.View(AgentFixtures.RevisedStudent);
        env.AgentReturns(revised);
        await env.Poller.PollOnceAsync();

        // The room is looked up by the proposal's room_id (the source of truth), not taken from the agent's text.
        var roomId = revised.ReadProposal()!.RoomId;
        var code = await env.QueryAsync(db => db.Rooms.Where(r => r.Id == roomId).Select(r => r.Code).SingleAsync());
        var latest = (await GetJsonAsync(owner, $"{Url}/{requestId}")).GetProperty("latestProposal");
        (latest.GetProperty("revisionNo").GetInt32(), latest.GetProperty("roomId").GetInt64(), latest.GetProperty("roomCode").GetString(),
                latest.GetProperty("quoteStatus").GetString())
            .Should().Be((2, roomId, code, QuotationStatuses.Draft));
    }

    [Fact]
    public async Task LatestProposal_after_reject_is_null()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "reject", new { reason = "Not this term" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var owner = await OwnerAsync(env, requestId);

        (await GetJsonAsync(owner, $"{Url}/{requestId}")).GetProperty("latestProposal").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
