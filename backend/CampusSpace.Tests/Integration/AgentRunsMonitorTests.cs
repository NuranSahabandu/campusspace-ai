using System.Net;
using System.Text.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The agent runs monitor (UC23): GET /api/agent-runs and /api/agent-runs/metrics. Runs, steps and decisions are inserted
/// directly with known numbers, each test on its own database, so every count, average and p95 is exact.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AgentRunsMonitorTests(PostgresFixture fixture)
{
    private const string List = "/api/agent-runs";
    private const string Metrics = "/api/agent-runs/metrics";

    /// <summary>The campus day every run of the main dataset is created on.</summary>
    private static readonly DateOnly Day = new(2026, 9, 15);

    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0) => CampusTime.At(date, new TimeOnly(hour, minute));

    // ---------- step outputs, as the agent service stores them ----------

    private const string StubPlan = """{"steps":[],"planner_fallback":false}""";
    private static string LlmPlan(int tokens) =>
        $$$"""{"steps":[],"planner_fallback":false,"planner":"llm","attempts":1,"usage":{"total_tokens":{{{tokens}}},"llm_calls":1,"estimated":false}}""";
    private const string FallbackPlan =
        """{"steps":[],"planner_fallback":true,"planner":"fallback","attempts":2,"usage":{"total_tokens":300,"llm_calls":2,"estimated":false},"fallback_reason":"invalid"}""";
    private static string LlmWorker(int tokens) =>
        $$$"""{"options":[],"mode":"llm","attempts":1,"worker_fallback":false,"usage":{"total_tokens":{{{tokens}}},"llm_calls":2,"estimated":false}}""";
    private const string FallbackWorker =
        """{"quote":null,"mode":"fallback","attempts":0,"worker_fallback":true,"usage":null,"fallback_reason":"run time budget exhausted"}""";
    private const string SkippedWorker = """{"lines":[],"mode":"llm","attempts":0,"worker_fallback":false,"usage":null}""";
    private const string StubWorker = """{"options":[],"unmet":null}""";
    private const string Code = """{"outcome":"pass","failed":[]}""";

    private sealed record Step(string Agent, int Ms, string? Output, string Status = AgentStepStatuses.Succeeded, int ToolCalls = 0);

    private static Step Failed(string agent, int ms) => new(agent, ms, null, AgentStepStatuses.Failed);

    private sealed class Env(CustomWebApplicationFactory factory, HttpClient officer, long requesterId, long officerId) : IAsyncDisposable
    {
        public CustomWebApplicationFactory Factory { get; } = factory;
        public HttpClient Officer { get; } = officer;

        /// <summary>A run (on a request of its own) with its steps and decisions, created at <paramref name="created"/>.</summary>
        public async Task<(Guid RunId, long RequestId)> RunAsync(
            string status, DateTimeOffset created, Step[]? steps = null, string[]? decisions = null, string? purpose = null,
            int? durationMs = null, string? failureReason = null, string? proposalJson = null, string? model = null)
        {
            var requestId = await BookingRequestTestData.InsertSubmittedAsync(Factory, requesterId);
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (purpose is not null)
                await db.BookingRequests.Where(r => r.Id == requestId).ExecuteUpdateAsync(u => u.SetProperty(r => r.Purpose, purpose));

            var run = new AgentRun
            {
                Id = Guid.NewGuid(), RequestId = requestId, Status = status, DurationMs = durationMs, Model = model,
                FailureReason = failureReason ?? (status == AgentRunStatuses.Failed ? "test failure" : null),
                ProposalJson = proposalJson,
                Steps = (steps ?? []).Select((s, i) => new AgentStep
                {
                    Sequence = i + 1, AgentName = s.Agent, Status = s.Status, DurationMs = s.Ms, OutputJson = s.Output,
                    Error = s.Status == AgentStepStatuses.Failed ? "worker failed" : null,
                    ToolCalls = Enumerable.Range(0, s.ToolCalls).Select(_ => new AgentToolCall
                    {
                        ToolName = "search_available_rooms", ArgsJson = "{}", Succeeded = true, DurationMs = 5,
                    }).ToList(),
                }).ToList(),
            };
            db.AgentRuns.Add(run);
            // Decisions in the order given, a minute apart: the last one is the run's latest decision.
            foreach (var (decision, i) in (decisions ?? []).Select((d, i) => (d, i)))
                db.ApprovalDecisions.Add(new ApprovalDecision
                {
                    RequestId = requestId, AgentRunId = run.Id, OfficerId = officerId, Decision = decision,
                    Comment = decision == ApprovalDecisions.Approve ? null : "please change",
                    DecidedAt = created.UtcDateTime.AddMinutes(i + 1),
                });
            await db.SaveChangesAsync();
            // SaveChanges stamps CreatedAt with now; the monitor filters on it, so set it afterwards.
            await db.AgentRuns.Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.CreatedAt, created.UtcDateTime));
            return (run.Id, requestId);
        }

        public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
    }

    private async Task<Env> EnvAsync()
    {
        var factory = await fixture.CreateIsolatedFactoryAsync();
        var (_, requesterId) = await TestAuth.CreateUserClientAsync(factory, Roles.Student);
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        return new Env(factory, officer, requesterId, officerId);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.ReadJsonAsync();
    }

    private static List<string> Purposes(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("purpose").GetString()!).ToList();

    // ---------- authorization ----------

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Lecturer)]
    [InlineData(Roles.LabTechnician)]
    [InlineData(Roles.Admin)]
    public async Task Only_Facilities_Officers_read_the_monitor(string role)
    {
        var client = TestAuth.CreateClient(fixture.Factory, role);
        foreach (var url in new[] { List, Metrics })
            await (await client.GetAsync(url)).ShouldBeProblemAsync(403);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var client = fixture.Factory.CreateClient();
        foreach (var url in new[] { List, Metrics })
            (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- list ----------

    [Fact]
    public async Task List_shows_counts_tokens_fallback_and_a_cut_failure_reason_newest_first()
    {
        await using var env = await EnvAsync();
        var longReason = new string('x', 250);
        var (llmRun, llmRequest) = await env.RunAsync(AgentRunStatuses.Completed, At(Day, 10), purpose: "LLM run",
            steps: [new("supervisor", 100, LlmPlan(1000)), new("venue_matching", 200, LlmWorker(500), ToolCalls: 2),
                    new("policy_cost", 300, FallbackWorker, ToolCalls: 1)],
            decisions: [ApprovalDecisions.Approve], durationMs: 90_000, model: "planner=gemini-3.5-flash; workers=stub");
        await env.RunAsync(AgentRunStatuses.Failed, At(Day, 11), purpose: "Stub run",
            steps: [new("supervisor", 100, StubPlan)], failureReason: longReason);

        var page = await GetJsonAsync(env.Officer, List);

        page.GetProperty("total").GetInt32().Should().Be(2);
        Purposes(page).Should().Equal("Stub run", "LLM run");
        var stub = page.GetProperty("items")[0];
        stub.GetProperty("totalTokens").ValueKind.Should().Be(JsonValueKind.Null);
        stub.GetProperty("anyFallback").GetBoolean().Should().BeFalse();
        stub.GetProperty("failureReason").GetString().Should().Be(new string('x', 200) + "…");
        stub.GetProperty("durationMs").ValueKind.Should().Be(JsonValueKind.Null);

        var llm = page.GetProperty("items")[1];
        llm.GetProperty("id").GetGuid().Should().Be(llmRun);
        llm.GetProperty("requestId").GetInt64().Should().Be(llmRequest);
        llm.GetProperty("status").GetString().Should().Be(AgentRunStatuses.Completed);
        llm.GetProperty("stepCount").GetInt32().Should().Be(3);
        llm.GetProperty("toolCallCount").GetInt32().Should().Be(3);
        llm.GetProperty("totalTokens").GetInt64().Should().Be(1500);
        llm.GetProperty("anyFallback").GetBoolean().Should().BeTrue();
        llm.GetProperty("durationMs").GetInt32().Should().Be(90_000);
        llm.GetProperty("model").GetString().Should().Be("planner=gemini-3.5-flash; workers=stub");
        llm.GetProperty("failureReason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task List_filters_by_status_request_fallback_and_campus_dates()
    {
        await using var env = await EnvAsync();
        // Campus-day edges of [Day, Day + 1]: 00:00 on Day and 23:59 on Day + 1 are in; the minute either side is not.
        await env.RunAsync(AgentRunStatuses.Completed, At(Day.AddDays(-1), 23, 59), purpose: "before");
        await env.RunAsync(AgentRunStatuses.Completed, At(Day, 0), purpose: "first",
            steps: [new("venue_matching", 10, FallbackWorker)]);
        var (_, failedRequest) = await env.RunAsync(AgentRunStatuses.Failed, At(Day, 12), purpose: "failed");
        await env.RunAsync(AgentRunStatuses.Rejected, At(Day.AddDays(1), 23, 59), purpose: "last",
            steps: [new("supervisor", 10, FallbackPlan)]);
        await env.RunAsync(AgentRunStatuses.Completed, At(Day.AddDays(2), 0), purpose: "after");

        var range = $"from={Day:yyyy-MM-dd}&to={Day.AddDays(1):yyyy-MM-dd}";
        Purposes(await GetJsonAsync(env.Officer, $"{List}?{range}")).Should().Equal("last", "failed", "first");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?status=Failed&status=Rejected"))
            .Should().Equal("last", "failed");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?status=Failed,Rejected"))
            .Should().Equal("last", "failed");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?requestId={failedRequest}")).Should().Equal("failed");
        // Worker and planner fallbacks both count.
        Purposes(await GetJsonAsync(env.Officer, $"{List}?fallback=true")).Should().Equal("last", "first");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?fallback=false")).Should().Equal("after", "failed", "before");
    }

    [Fact]
    public async Task Search_matches_the_purpose_with_LIKE_wildcards_taken_literally()
    {
        await using var env = await EnvAsync();
        await env.RunAsync(AgentRunStatuses.Completed, At(Day, 9), purpose: "Robotics demo at 50% capacity");
        await env.RunAsync(AgentRunStatuses.Completed, At(Day, 10), purpose: "Budget for 500 seats");
        await env.RunAsync(AgentRunStatuses.Completed, At(Day, 11), purpose: "Club a_b night");
        await env.RunAsync(AgentRunStatuses.Completed, At(Day, 12), purpose: "Club axb night");

        Purposes(await GetJsonAsync(env.Officer, $"{List}?search=50%25")).Should().Equal("Robotics demo at 50% capacity");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?search=a_b")).Should().Equal("Club a_b night");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?search=CLUB")).Should().Equal("Club axb night", "Club a_b night");
    }

    [Fact]
    public async Task List_sorts_and_pages()
    {
        await using var env = await EnvAsync();
        await env.RunAsync(AgentRunStatuses.Failed, At(Day, 9), purpose: "a", durationMs: 300);
        await env.RunAsync(AgentRunStatuses.Completed, At(Day, 10), purpose: "b", durationMs: 100);
        await env.RunAsync(AgentRunStatuses.Running, At(Day, 11), purpose: "c");
        await env.RunAsync(AgentRunStatuses.AwaitingApproval, At(Day, 12), purpose: "d", durationMs: 200);

        Purposes(await GetJsonAsync(env.Officer, $"{List}?sort=createdAt")).Should().Equal("a", "b", "c", "d");
        // Runs without a duration go last both ways.
        Purposes(await GetJsonAsync(env.Officer, $"{List}?sort=durationMs")).Should().Equal("b", "d", "a", "c");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?sort=-durationMs")).Should().Equal("a", "d", "b", "c");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?sort=status")).Should().Equal("d", "b", "a", "c");
        Purposes(await GetJsonAsync(env.Officer, $"{List}?sort=-status")).Should().Equal("c", "a", "b", "d");

        var page = await GetJsonAsync(env.Officer, $"{List}?page=2&pageSize=3");
        page.GetProperty("total").GetInt32().Should().Be(4);
        page.GetProperty("page").GetInt32().Should().Be(2);
        page.GetProperty("pageSize").GetInt32().Should().Be(3);
        Purposes(page).Should().Equal("a");
    }

    [Theory]
    [InlineData(List + "?status=Bogus", "status")]
    [InlineData(List + "?sort=purpose", "sort")]
    [InlineData(List + "?from=2026-09-20&to=2026-09-19", "from")]
    [InlineData(List + "?requestId=0", "requestId")]
    [InlineData(Metrics + "?from=2026-09-20&to=2026-09-19", "from")]
    public async Task Invalid_filters_are_400_on_the_field(string url, string field)
    {
        var client = TestAuth.CreateClient(fixture.Factory, Roles.FacilitiesOfficer);
        var problem = await (await client.GetAsync(url)).ShouldBeProblemAsync(400);
        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name)
            .Should().Contain(n => string.Equals(n, field, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- metrics ----------

    /// <summary>
    /// Ten runs on Day plus one outside the range. Expected values are worked out by hand in the comments
    /// (p95 = percentile_cont: the value at position 0.95 × (n − 1) of the sorted list, interpolated).
    /// </summary>
    private static async Task SeedMetricsAsync(Env env)
    {
        // R1 Completed after a revise then an approve. Processing = 100+200+50+300+10 = 660 (finalize 40 left out).
        await env.RunAsync(AgentRunStatuses.Completed, At(Day, 8), decisions: [ApprovalDecisions.Approve], steps:
        [
            new("supervisor", 100, LlmPlan(1000)), new("venue_matching", 200, LlmWorker(500)),
            new("equipment_allocation", 50, SkippedWorker), new("policy_cost", 300, FallbackWorker),
            new("validate", 10, Code), new("finalize", 40, Code),
        ]);
        // R2 reached the gate, then failed at approval (latest decision Approve, after a Revise). Processing 220.
        await env.RunAsync(AgentRunStatuses.Failed, At(Day, 9), decisions: [ApprovalDecisions.Revise, ApprovalDecisions.Approve],
            failureReason: "The proposal is no longer valid: room busy", steps:
            [new("supervisor", 120, StubPlan), new("venue_matching", 80, StubWorker), new("validate", 20, Code), new("finalize", 500, Code)]);
        // R3 failed by the watchdog with no steps: a failure, but in no latency average.
        await env.RunAsync(AgentRunStatuses.Failed, At(Day, 10), failureReason: "Agent service unreachable");
        // R4 failed before the gate: planner fell back, then the venue worker failed. Processing 1000, tokens 300.
        await env.RunAsync(AgentRunStatuses.Failed, At(Day, 11), steps:
            [new("supervisor", 400, FallbackPlan), Failed("venue_matching", 600)]);
        // R5 revised and awaiting approval again: both cycles count. Processing 420, tokens 500.
        await env.RunAsync(AgentRunStatuses.AwaitingApproval, At(Day, 12), decisions: [ApprovalDecisions.Revise], steps:
        [
            new("supervisor", 100, StubPlan), new("venue_matching", 100, LlmWorker(200)), new("validate", 10, Code),
            new("supervisor", 100, StubPlan), new("venue_matching", 100, LlmWorker(300)), new("validate", 10, Code),
        ]);
        // R6 the revise cycle failed: before the gate, even though the old proposal is still stored. Processing 100.
        await env.RunAsync(AgentRunStatuses.Failed, At(Day, 13), decisions: [ApprovalDecisions.Revise],
            proposalJson: """{"venue":{"options":[]},"equipment":null}""", steps:
            [new("supervisor", 50, StubPlan), new("validate", 10, Code), Failed("supervisor", 40)]);
        // R7 resuming for a revise: still agent work, in neither side of the success rate.
        await env.RunAsync(AgentRunStatuses.Resuming, At(Day, 14), decisions: [ApprovalDecisions.Revise],
            steps: [new("supervisor", 70, StubPlan)]);
        // R8 rejected by the officer. Processing 100.
        await env.RunAsync(AgentRunStatuses.Rejected, At(Day, 15), decisions: [ApprovalDecisions.Reject],
            steps: [new("supervisor", 90, StubPlan), new("validate", 10, Code)]);
        // R9 cancelled while awaiting approval (cancel writes no decision). Processing 30.
        await env.RunAsync(AgentRunStatuses.Cancelled, At(Day, 16), steps: [new("supervisor", 30, StubPlan)]);
        // R10 running, no steps yet.
        await env.RunAsync(AgentRunStatuses.Running, At(Day, 17));
        // Outside the range: must not count anywhere.
        await env.RunAsync(AgentRunStatuses.Completed, At(Day.AddDays(-1), 23, 59), decisions: [ApprovalDecisions.Approve],
            steps: [new("supervisor", 9999, LlmPlan(9999)), new("ghost_agent", 1, Code)]);
    }

    [Fact]
    public async Task Metrics_per_run_use_gate_evidence_and_give_every_denominator()
    {
        await using var env = await EnvAsync();
        await SeedMetricsAsync(env);

        var m = await GetJsonAsync(env.Officer, $"{Metrics}?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}");
        m.GetProperty("from").GetString().Should().Be($"{Day:yyyy-MM-dd}");
        var runs = m.GetProperty("runs");

        runs.GetProperty("total").GetInt32().Should().Be(10);
        var byStatus = runs.GetProperty("byStatus");
        byStatus.EnumerateObject().Select(p => p.Name).Should().Equal(AgentRunStatuses.All);
        byStatus.GetProperty("Failed").GetInt32().Should().Be(4);
        byStatus.GetProperty("Queued").GetInt32().Should().Be(0);
        byStatus.GetProperty("Completed").GetInt32().Should().Be(1);

        runs.GetProperty("inProgress").GetInt32().Should().Be(2);        // R7, R10
        runs.GetProperty("finished").GetInt32().Should().Be(8);
        runs.GetProperty("reachedGate").GetInt32().Should().Be(5);       // R1, R2 (failed at approval), R5, R8, R9
        runs.GetProperty("failedBeforeGate").GetInt32().Should().Be(3);  // R3, R4, R6
        runs.GetProperty("successRate").GetDouble().Should().Be(0.625);

        // Reached the gate: [30, 100, 220, 420, 660] → avg 286, p95 at 3.8 = 420 + 0.8 × 240 = 612.
        var gate = runs.GetProperty("reachedGateProcessing");
        gate.GetProperty("runs").GetInt32().Should().Be(5);
        gate.GetProperty("withoutSteps").GetInt32().Should().Be(0);
        gate.GetProperty("avgMs").GetInt32().Should().Be(286);
        gate.GetProperty("p95Ms").GetInt32().Should().Be(612);

        // Failed before the gate: [100, 1000] (R3 has no steps) → avg 550, p95 at 0.95 = 100 + 0.95 × 900 = 955.
        var failed = runs.GetProperty("failedBeforeGateProcessing");
        failed.GetProperty("runs").GetInt32().Should().Be(2);
        failed.GetProperty("withoutSteps").GetInt32().Should().Be(1);
        failed.GetProperty("avgMs").GetInt32().Should().Be(550);
        failed.GetProperty("p95Ms").GetInt32().Should().Be(955);

        // Tokens: R1 1500, R4 300, R5 500 (both cycles).
        runs.GetProperty("runsWithUsage").GetInt32().Should().Be(3);
        runs.GetProperty("totalTokens").GetInt64().Should().Be(2300);
        runs.GetProperty("avgTokensPerRun").GetDouble().Should().Be(766.7);

        // LLM attempted: R1, R4, R5; fell back: R1, R4.
        runs.GetProperty("llmAttemptedRuns").GetInt32().Should().Be(3);
        runs.GetProperty("fallbackRuns").GetInt32().Should().Be(2);
        runs.GetProperty("fallbackRate").GetDouble().Should().Be(0.6667);
    }

    [Fact]
    public async Task Metrics_per_agent_classify_each_step_once()
    {
        await using var env = await EnvAsync();
        await SeedMetricsAsync(env);

        var agents = (await GetJsonAsync(env.Officer, $"{Metrics}?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}"))
            .GetProperty("agents").EnumerateArray().ToList();

        agents.Select(a => a.GetProperty("agent").GetString())
            .Should().Equal("supervisor", "venue_matching", "equipment_allocation", "policy_cost", "validate", "finalize");

        // supervisor: [30, 40, 50, 70, 90, 100, 100, 100, 120, 400] → avg 110, p95 at 8.55 = 120 + 0.55 × 280 = 274.
        // 1 failed, 1 llm (R1), 1 fallback (R4), 8 stub; tokens 1000 + 300.
        Expect(agents[0], steps: 10, avg: 110, p95: 274, failed: 1, failureRate: 0.1, llm: 1, llmShare: 0.1, skipped: 0,
            attempted: 2, fallback: 1, fallbackRate: 0.5, withUsage: 2, tokens: 1300, avgTokens: 650);
        // venue_matching: [80, 100, 100, 200, 600] → avg 216, p95 at 3.8 = 200 + 0.8 × 400 = 520. 3 llm, 1 failed.
        Expect(agents[1], steps: 5, avg: 216, p95: 520, failed: 1, failureRate: 0.2, llm: 3, llmShare: 0.6, skipped: 0,
            attempted: 3, fallback: 0, fallbackRate: 0, withUsage: 3, tokens: 1000, avgTokens: 333.3);
        // equipment_allocation: one skipped step (mode llm, no model call): not LLM-attempted, so no fallback rate.
        Expect(agents[2], steps: 1, avg: 50, p95: 50, failed: 0, failureRate: 0, llm: 0, llmShare: 0, skipped: 1,
            attempted: 0, fallback: 0, fallbackRate: null, withUsage: 0, tokens: 0, avgTokens: null);
        // policy_cost: one budget-exhausted fallback (attempts 0, still a fallback).
        Expect(agents[3], steps: 1, avg: 300, p95: 300, failed: 0, failureRate: 0, llm: 0, llmShare: 0, skipped: 0,
            attempted: 1, fallback: 1, fallbackRate: 1, withUsage: 0, tokens: 0, avgTokens: null);
        // validate: [10, 10, 10, 10, 10, 20] → avg 11.67 ≈ 12, p95 at 4.75 = 17.5 ≈ 18.
        Expect(agents[4], steps: 6, avg: 12, p95: 18, failed: 0, failureRate: 0, llm: 0, llmShare: 0, skipped: 0,
            attempted: 0, fallback: 0, fallbackRate: null, withUsage: 0, tokens: 0, avgTokens: null);
        // finalize: [40, 500] → avg 270, p95 at 0.95 = 40 + 0.95 × 460 = 477.
        Expect(agents[5], steps: 2, avg: 270, p95: 477, failed: 0, failureRate: 0, llm: 0, llmShare: 0, skipped: 0,
            attempted: 0, fallback: 0, fallbackRate: null, withUsage: 0, tokens: 0, avgTokens: null);
    }

    private static void Expect(
        JsonElement a, int steps, int avg, int p95, int failed, double failureRate, int llm, double llmShare, int skipped,
        int attempted, int fallback, double? fallbackRate, int withUsage, long tokens, double? avgTokens)
    {
        var name = a.GetProperty("agent").GetString();
        a.GetProperty("steps").GetInt32().Should().Be(steps, name);
        a.GetProperty("avgMs").GetInt32().Should().Be(avg, name);
        a.GetProperty("p95Ms").GetInt32().Should().Be(p95, name);
        a.GetProperty("failedSteps").GetInt32().Should().Be(failed, name);
        a.GetProperty("failureRate").GetDouble().Should().Be(failureRate, name);
        a.GetProperty("llmSteps").GetInt32().Should().Be(llm, name);
        a.GetProperty("llmShare").GetDouble().Should().Be(llmShare, name);
        a.GetProperty("skippedSteps").GetInt32().Should().Be(skipped, name);
        a.GetProperty("llmAttemptedSteps").GetInt32().Should().Be(attempted, name);
        a.GetProperty("fallbackSteps").GetInt32().Should().Be(fallback, name);
        Nullable(a.GetProperty("fallbackRate")).Should().Be(fallbackRate, name);
        a.GetProperty("stepsWithUsage").GetInt32().Should().Be(withUsage, name);
        a.GetProperty("totalTokens").GetInt64().Should().Be(tokens, name);
        Nullable(a.GetProperty("avgTokensPerStep")).Should().Be(avgTokens, name);
    }

    private static double? Nullable(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetDouble();

    [Fact]
    public async Task Without_a_range_metrics_cover_every_run()
    {
        await using var env = await EnvAsync();
        await SeedMetricsAsync(env);

        var m = await GetJsonAsync(env.Officer, Metrics);

        m.GetProperty("from").ValueKind.Should().Be(JsonValueKind.Null);
        m.GetProperty("runs").GetProperty("total").GetInt32().Should().Be(11);
        m.GetProperty("agents").EnumerateArray().Select(a => a.GetProperty("agent").GetString())
            .Should().EndWith("ghost_agent", "unknown agent names follow the graph's order");
    }

    [Fact]
    public async Task An_empty_range_gives_zero_counts_zero_denominators_and_null_rates()
    {
        await using var env = await EnvAsync();
        await SeedMetricsAsync(env);

        var m = await GetJsonAsync(env.Officer, $"{Metrics}?from=2030-01-01&to=2030-01-07");
        var runs = m.GetProperty("runs");

        m.GetProperty("agents").GetArrayLength().Should().Be(0);
        foreach (var field in new[] { "total", "inProgress", "finished", "reachedGate", "failedBeforeGate", "runsWithUsage",
                     "llmAttemptedRuns", "fallbackRuns" })
            runs.GetProperty(field).GetInt32().Should().Be(0, field);
        runs.GetProperty("totalTokens").GetInt64().Should().Be(0);
        runs.GetProperty("byStatus").EnumerateObject().Should().OnlyContain(p => p.Value.GetInt32() == 0);
        foreach (var field in new[] { "successRate", "avgTokensPerRun", "fallbackRate" })
            runs.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null, field);
        foreach (var bucket in new[] { "reachedGateProcessing", "failedBeforeGateProcessing" })
        {
            var b = runs.GetProperty(bucket);
            b.GetProperty("runs").GetInt32().Should().Be(0);
            b.GetProperty("withoutSteps").GetInt32().Should().Be(0);
            b.GetProperty("avgMs").ValueKind.Should().Be(JsonValueKind.Null);
            b.GetProperty("p95Ms").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }
}
