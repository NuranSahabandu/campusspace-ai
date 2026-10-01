using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.AgentRuns;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CampusSpace.Api.Services;

public sealed class AgentRunMonitorService(AppDbContext db) : IAgentRunMonitorService
{
    /// <summary>Jsonb containment (@>) patterns for "this step fell back to its stub".</summary>
    private const string WorkerFallback = """{"worker_fallback":true}""";
    private const string PlannerFallback = """{"planner_fallback":true}""";

    /// <summary>The only step that runs after the officer's decision; it is not agent work for the proposal.</summary>
    public const string FinalizeStep = "finalize";

    /// <summary>The graph's order, for the per-agent table; any other name follows alphabetically.</summary>
    public static readonly IReadOnlyList<string> AgentOrder =
        ["supervisor", "venue_matching", "equipment_allocation", "policy_cost", "validate", FinalizeStep];

    public async Task<PagedResult<AgentRunListItemDto>> ListAsync(AgentRunsQuery query, CancellationToken ct = default)
    {
        var runs = db.AgentRuns.AsNoTracking();

        var statuses = query.Statuses();
        if (statuses.Count > 0)
            runs = runs.Where(r => statuses.Contains(r.Status));
        var (fromUtc, toUtc) = Bounds(query.From, query.To);
        if (query.From is not null)
            runs = runs.Where(r => r.CreatedAt >= fromUtc);
        if (query.To is not null)
            runs = runs.Where(r => r.CreatedAt < toUtc);
        if (query.RequestId is { } requestId)
            runs = runs.Where(r => r.RequestId == requestId);
        if (query.Fallback is { } fallback)
            runs = runs.Where(r => r.Steps.Any(s =>
                EF.Functions.JsonContains(s.OutputJson!, WorkerFallback)
                || EF.Functions.JsonContains(s.OutputJson!, PlannerFallback)) == fallback);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = query.Search.ToContainsPattern();
            runs = runs.Where(r => EF.Functions.ILike(r.Request.Purpose, pattern, QueryableExtensions.LikeEscape));
        }

        var rows = runs.Select(r => new ListRow
        {
            Id = r.Id,
            RequestId = r.RequestId,
            Purpose = r.Request.Purpose,
            RevisionNo = r.RevisionNo,
            Status = r.Status,
            Model = r.Model,
            StartedAt = r.StartedAt,
            CompletedAt = r.CompletedAt,
            DurationMs = r.DurationMs,
            // One character more than shown, so the cut below knows whether anything was left out.
            FailureReason = r.FailureReason == null
                ? null
                : r.FailureReason.Substring(0, AgentRunListItemDto.FailureReasonMaxLength + 1),
            StepCount = r.Steps.Count,
            ToolCallCount = r.Steps.SelectMany(s => s.ToolCalls).Count(),
            AnyFallback = r.Steps.Any(s =>
                EF.Functions.JsonContains(s.OutputJson!, WorkerFallback)
                || EF.Functions.JsonContains(s.OutputJson!, PlannerFallback)),
            CreatedAt = r.CreatedAt,
        });

        // Id breaks ties so paging is stable. Runs with no duration yet sort last either way.
        rows = query.Sort switch
        {
            "createdAt" => rows.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "durationMs" => rows.OrderBy(x => x.DurationMs == null).ThenBy(x => x.DurationMs).ThenBy(x => x.Id),
            "-durationMs" => rows.OrderBy(x => x.DurationMs == null).ThenByDescending(x => x.DurationMs).ThenBy(x => x.Id),
            "status" => rows.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            "-status" => rows.OrderByDescending(x => x.Status).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => rows.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
        };

        var page = await rows.ToPagedResultAsync(query, ct);

        // Usage lives inside the step output (jsonb), so the tokens are summed in SQL for this page's runs only.
        var ids = page.Items.Select(x => x.Id).ToArray();
        var tokens = ids.Length == 0
            ? new Dictionary<Guid, long>()
            : (await db.Database.SqlQueryRaw<RunTokensRow>(PageTokensSql, new NpgsqlParameter("ids", ids)).ToListAsync(ct))
            .ToDictionary(x => x.RunId, x => x.Tokens);

        var items = page.Items.Select(x => new AgentRunListItemDto(
            x.Id, x.RequestId, x.Purpose, x.RevisionNo, x.Status, x.Model, x.StartedAt, x.CompletedAt, x.DurationMs,
            Cut(x.FailureReason), x.StepCount, x.ToolCallCount,
            tokens.TryGetValue(x.Id, out var t) ? t : null, x.AnyFallback, x.CreatedAt)).ToList();
        return new PagedResult<AgentRunListItemDto>(items, page.Page, page.PageSize, page.Total);
    }

    public async Task<AgentRunMetricsDto> GetMetricsAsync(AgentRunMetricsQuery query, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = Bounds(query.From, query.To);
        NpgsqlParameter[] Range() => [new("from", fromUtc), new("to", toUtc)];

        var byStatus = await db.AgentRuns.AsNoTracking()
            .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt < toUtc)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var agentRows = await db.Database.SqlQueryRaw<AgentRow>(AgentSql, Range()).ToListAsync(ct);
        var run = (await db.Database.SqlQueryRaw<RunRow>(RunSql, Range()).ToListAsync(ct)).Single();

        var agents = agentRows
            .OrderBy(a => AgentOrder.Contains(a.Agent) ? AgentOrder.ToList().IndexOf(a.Agent) : AgentOrder.Count)
            .ThenBy(a => a.Agent, StringComparer.Ordinal)
            .Select(a =>
            {
                var attempted = a.LlmSteps + a.FallbackSteps;
                return new AgentMetricsDto(
                    a.Agent, a.Steps, Ms(a.AvgMs), Ms(a.P95Ms),
                    a.FailedSteps, Rate(a.FailedSteps, a.Steps),
                    a.LlmSteps, Rate(a.LlmSteps, a.Steps), a.SkippedSteps,
                    attempted, a.FallbackSteps, Rate(a.FallbackSteps, attempted),
                    a.StepsWithUsage, a.TotalTokens, Average(a.TotalTokens, a.StepsWithUsage));
            })
            .ToList();

        var total = byStatus.Values.Sum();
        var runs = new RunMetricsDto(
            total,
            AgentRunStatuses.All.ToDictionary(s => s, s => byStatus.GetValueOrDefault(s)),
            run.InProgress, run.Finished, run.ReachedGate, run.Finished - run.ReachedGate,
            Rate(run.ReachedGate, run.Finished),
            new ProcessingTimeDto(run.GateRuns, run.GateWithoutSteps, Ms(run.GateAvgMs), Ms(run.GateP95Ms)),
            new ProcessingTimeDto(run.FailedRuns, run.FailedWithoutSteps, Ms(run.FailedAvgMs), Ms(run.FailedP95Ms)),
            run.RunsWithUsage, run.TotalTokens, Average(run.TotalTokens, run.RunsWithUsage),
            run.LlmAttemptedRuns, run.FallbackRuns, Rate(run.FallbackRuns, run.LlmAttemptedRuns));

        return new AgentRunMetricsDto(query.From, query.To, runs, agents);
    }

    // ---------- SQL ----------
    // Only code constants are spliced into these strings; the date range is always a parameter.

    private static string Json(string key) => $"""s."OutputJson" -> '{key}'""";

    /// <summary>
    /// A step's mode, exactly one per step. fallback: worker_fallback or planner_fallback is true. llm: mode (worker) or
    /// planner (supervisor) is "llm" and the model was called (attempts &gt; 0). skipped: an LLM agent that needed no
    /// model call (attempts 0). stub: everything else, including code steps and failed steps (null output). Jsonb
    /// comparisons, never casts, so an odd value can't make the query fail.
    /// </summary>
    private static readonly string StepMode = $"""
        CASE
          WHEN {Json("worker_fallback")} = 'true'::jsonb OR {Json("planner_fallback")} = 'true'::jsonb THEN 'fallback'
          WHEN COALESCE(s."OutputJson" ->> 'mode', s."OutputJson" ->> 'planner') = 'llm'
               AND {Json("attempts")} > '0'::jsonb THEN 'llm'
          WHEN COALESCE(s."OutputJson" ->> 'mode', s."OutputJson" ->> 'planner') = 'llm' THEN 'skipped'
          ELSE 'stub'
        END
        """;

    /// <summary>A step's usage.total_tokens, or null when it has none (the cast only runs on a JSON number).</summary>
    private static readonly string StepTokens = $"""
        (CASE WHEN jsonb_typeof({Json("usage")} -> 'total_tokens') = 'number'
              THEN round(({Json("usage")} ->> 'total_tokens')::numeric) END)
        """;

    /// <summary>Σ tokens per run for one page of the list (runs without usage are left out, so they show null).</summary>
    private static readonly string PageTokensSql = $"""
        SELECT s."RunId", sum({StepTokens})::bigint AS "Tokens"
        FROM "AgentSteps" s
        WHERE s."RunId" = ANY(@ids)
        GROUP BY s."RunId"
        HAVING count({StepTokens}) > 0
        """;

    private const string InRange = """r."CreatedAt" >= @from AND r."CreatedAt" < @to""";

    private static readonly string AgentSql = $"""
        WITH steps AS (
          SELECT s."AgentName" AS agent, s."Status" AS status, s."DurationMs" AS ms,
                 {StepMode} AS mode, {StepTokens} AS tokens
          FROM "AgentSteps" s
          JOIN "AgentRuns" r ON r."Id" = s."RunId"
          WHERE {InRange}
        )
        SELECT agent AS "Agent",
               count(*)::int AS "Steps",
               avg(ms)::float8 AS "AvgMs",
               percentile_cont(0.95) WITHIN GROUP (ORDER BY ms)::float8 AS "P95Ms",
               count(*) FILTER (WHERE status = '{AgentStepStatuses.Failed}')::int AS "FailedSteps",
               count(*) FILTER (WHERE mode = 'llm')::int AS "LlmSteps",
               count(*) FILTER (WHERE mode = 'skipped')::int AS "SkippedSteps",
               count(*) FILTER (WHERE mode = 'fallback')::int AS "FallbackSteps",
               count(tokens)::int AS "StepsWithUsage",
               COALESCE(sum(tokens), 0)::bigint AS "TotalTokens"
        FROM steps
        GROUP BY agent
        """;

    /// <summary>
    /// One row. A run's latest decision is its newest ApprovalDecisions row. reached: the status proves the gate
    /// (Rejected and Cancelled are only set on an AwaitingApproval run, Completed only after an approval), or the latest
    /// decision is Approve (a decision is only possible at the gate; this covers a failed approval). in_progress: still
    /// agent work (a Resuming revise is a new plan cycle). Not Nodes ('human_gate' is added only after the resume) and
    /// not ProposalJson (kept through a revise cycle that then failed).
    /// </summary>
    private static readonly string RunSql = $"""
        WITH runs AS (
          SELECT r."Id", r."Status",
                 (SELECT d."Decision" FROM "ApprovalDecisions" d WHERE d."AgentRunId" = r."Id"
                  ORDER BY d."DecidedAt" DESC, d."Id" DESC LIMIT 1) AS last_decision
          FROM "AgentRuns" r
          WHERE {InRange}
        ),
        classified AS (
          SELECT "Id",
                 ("Status" IN ({SqlList(AgentRunStatuses.AwaitingApproval, AgentRunStatuses.Completed, AgentRunStatuses.Rejected, AgentRunStatuses.Cancelled)})
                  OR COALESCE(last_decision = '{ApprovalDecisions.Approve}', false)) AS reached,
                 ("Status" IN ({SqlList(AgentRunStatuses.Queued, AgentRunStatuses.Running)})
                  OR ("Status" = '{AgentRunStatuses.Resuming}' AND COALESCE(last_decision = '{ApprovalDecisions.Revise}', false))) AS in_progress
          FROM runs
        ),
        steps AS (
          SELECT s."RunId", s."AgentName", s."DurationMs", {StepMode} AS mode, {StepTokens} AS tokens
          FROM "AgentSteps" s
          JOIN runs ON runs."Id" = s."RunId"
        ),
        per_run AS (
          SELECT "RunId",
                 sum("DurationMs") FILTER (WHERE "AgentName" <> '{FinalizeStep}') AS ms,
                 count(*) FILTER (WHERE "AgentName" <> '{FinalizeStep}') AS counted,
                 sum(tokens) AS tokens,
                 count(tokens) AS with_usage,
                 bool_or(mode IN ('llm', 'fallback')) AS any_llm,
                 bool_or(mode = 'fallback') AS any_fallback
          FROM steps
          GROUP BY "RunId"
        ),
        joined AS (
          SELECT c.reached, c.in_progress,
                 (NOT c.in_progress AND NOT c.reached) AS failed,
                 COALESCE(p.counted, 0) AS counted, p.ms, p.tokens, COALESCE(p.with_usage, 0) AS with_usage,
                 COALESCE(p.any_llm, false) AS any_llm, COALESCE(p.any_fallback, false) AS any_fallback
          FROM classified c
          LEFT JOIN per_run p ON p."RunId" = c."Id"
        )
        SELECT count(*) FILTER (WHERE in_progress)::int AS "InProgress",
               count(*) FILTER (WHERE NOT in_progress)::int AS "Finished",
               count(*) FILTER (WHERE reached)::int AS "ReachedGate",
               count(*) FILTER (WHERE reached AND counted > 0)::int AS "GateRuns",
               count(*) FILTER (WHERE reached AND counted = 0)::int AS "GateWithoutSteps",
               (avg(ms) FILTER (WHERE reached AND counted > 0))::float8 AS "GateAvgMs",
               (percentile_cont(0.95) WITHIN GROUP (ORDER BY ms) FILTER (WHERE reached AND counted > 0))::float8 AS "GateP95Ms",
               count(*) FILTER (WHERE failed AND counted > 0)::int AS "FailedRuns",
               count(*) FILTER (WHERE failed AND counted = 0)::int AS "FailedWithoutSteps",
               (avg(ms) FILTER (WHERE failed AND counted > 0))::float8 AS "FailedAvgMs",
               (percentile_cont(0.95) WITHIN GROUP (ORDER BY ms) FILTER (WHERE failed AND counted > 0))::float8 AS "FailedP95Ms",
               count(*) FILTER (WHERE with_usage > 0)::int AS "RunsWithUsage",
               COALESCE(sum(tokens) FILTER (WHERE with_usage > 0), 0)::bigint AS "TotalTokens",
               count(*) FILTER (WHERE any_llm)::int AS "LlmAttemptedRuns",
               count(*) FILTER (WHERE any_fallback)::int AS "FallbackRuns"
        FROM joined
        """;

    private static string SqlList(params string[] values) => string.Join(", ", values.Select(v => $"'{v}'"));

    // ---------- helpers ----------

    /// <summary>
    /// [campus midnight of From, campus midnight after To) in UTC. A missing end is unbounded (Npgsql sends
    /// DateTime.MinValue/MaxValue as -infinity/infinity), so the SQL never needs a nullable parameter.
    /// </summary>
    private static (DateTime From, DateTime To) Bounds(DateOnly? from, DateOnly? to) => (
        from is { } f ? CampusTime.StartOf(f).UtcDateTime : DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
        to is { } t ? CampusTime.StartOf(t.AddDays(1)).UtcDateTime : DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    private static string? Cut(string? text) =>
        text is null || text.Length <= AgentRunListItemDto.FailureReasonMaxLength
            ? text
            : text[..AgentRunListItemDto.FailureReasonMaxLength] + "…";

    private static double? Rate(int numerator, int denominator) =>
        denominator == 0 ? null : Math.Round((double)numerator / denominator, 4);

    private static double? Average(long sum, int count) => count == 0 ? null : Math.Round((double)sum / count, 1);

    /// <summary>Whole milliseconds. percentile_cont works in float8, so 17.5 can arrive as 17.4999…: drop that noise first.</summary>
    private static int? Ms(double? value) =>
        value is { } v ? (int)Math.Round(Math.Round(v, 6), MidpointRounding.AwayFromZero) : null;

    private sealed class ListRow
    {
        public Guid Id { get; init; }
        public long RequestId { get; init; }
        public string Purpose { get; init; } = "";
        public int RevisionNo { get; init; }
        public string Status { get; init; } = "";
        public string? Model { get; init; }
        public DateTime? StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public int? DurationMs { get; init; }
        public string? FailureReason { get; init; }
        public int StepCount { get; init; }
        public int ToolCallCount { get; init; }
        public bool AnyFallback { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed class RunTokensRow
    {
        public Guid RunId { get; init; }
        public long Tokens { get; init; }
    }

    private sealed class AgentRow
    {
        public string Agent { get; init; } = "";
        public int Steps { get; init; }
        public double? AvgMs { get; init; }
        public double? P95Ms { get; init; }
        public int FailedSteps { get; init; }
        public int LlmSteps { get; init; }
        public int SkippedSteps { get; init; }
        public int FallbackSteps { get; init; }
        public int StepsWithUsage { get; init; }
        public long TotalTokens { get; init; }
    }

    private sealed class RunRow
    {
        public int InProgress { get; init; }
        public int Finished { get; init; }
        public int ReachedGate { get; init; }
        public int GateRuns { get; init; }
        public int GateWithoutSteps { get; init; }
        public double? GateAvgMs { get; init; }
        public double? GateP95Ms { get; init; }
        public int FailedRuns { get; init; }
        public int FailedWithoutSteps { get; init; }
        public double? FailedAvgMs { get; init; }
        public double? FailedP95Ms { get; init; }
        public int RunsWithUsage { get; init; }
        public long TotalTokens { get; init; }
        public int LlmAttemptedRuns { get; init; }
        public int FallbackRuns { get; init; }
    }
}
