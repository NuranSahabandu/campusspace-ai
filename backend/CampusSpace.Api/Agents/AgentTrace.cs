using System.Text.Json;
using System.Text.RegularExpressions;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Agents;

/// <summary>
/// Copies the agent service's trace onto an AgentRun (plan §11 step 5), for the poller and the approval finaliser. The copy
/// inserts only rows not stored yet (steps by Sequence, rules by Attempt + RuleCode), so repeating it never duplicates
/// anything. Nothing is saved here: the caller saves inside its own transaction.
/// </summary>
public static partial class AgentTrace
{
    /// <summary>Adds the steps (with their tool calls) and rule results not stored yet, and refreshes Nodes and Model.</summary>
    public static async Task CopyAsync(AppDbContext db, AgentRun run, AgentWorkflowView view, ILogger logger, CancellationToken ct)
    {
        var sequences = (await db.AgentSteps.Where(s => s.RunId == run.Id).Select(s => s.Sequence).ToListAsync(ct)).ToHashSet();
        foreach (var step in view.Steps ?? [])
        {
            if (step.Sequence < 1 || !sequences.Add(step.Sequence))
                continue;
            db.AgentSteps.Add(new AgentStep
            {
                RunId = run.Id,
                Sequence = step.Sequence,
                AgentName = NameOrUnknown(step.AgentName, AgentStepConfiguration.AgentNameMaxLength),
                Status = AgentStepStatuses.All.Contains(step.Status) ? step.Status : AgentStepStatuses.Failed,
                InputJson = Raw(step.Input),
                OutputJson = Raw(step.Output),
                Retries = Math.Max(step.Retries, 0),
                Error = step.Error,
                DurationMs = Math.Max(step.DurationMs, 0),
                ToolCalls = (step.ToolCalls ?? []).Select(c => new AgentToolCall
                {
                    ToolName = NameOrUnknown(c.ToolName, AgentToolCallConfiguration.ToolNameMaxLength),
                    ArgsJson = Raw(c.Args) ?? "{}",
                    ResultSummary = Raw(c.ResultSummary),
                    Succeeded = c.Succeeded,
                    Error = c.Succeeded ? c.Error : c.Error ?? "Tool call failed",
                    DurationMs = Math.Max(c.DurationMs, 0),
                }).ToList(),
            });
        }

        var rules = (await db.ValidationResults.Where(v => v.RunId == run.Id).Select(v => new { v.Attempt, v.RuleCode }).ToListAsync(ct))
            .Select(v => (v.Attempt, v.RuleCode)).ToHashSet();
        foreach (var rule in view.Validation ?? [])
        {
            if (rule.Attempt < 1 || !RuleCode().IsMatch(rule.Rule))
            {
                logger.LogWarning("Agent run {RunId}: skipped a validation row with attempt {Attempt} and rule {Rule}",
                    run.Id, rule.Attempt, Truncate(rule.Rule, 10));
                continue;
            }
            if (rules.Add((rule.Attempt, rule.Rule)))
                db.ValidationResults.Add(new AgentValidationResult
                {
                    RunId = run.Id, Attempt = rule.Attempt, RuleCode = rule.Rule, Passed = rule.Passed, Message = rule.Message,
                });
        }

        if (view.Nodes is { } nodes)
            run.Nodes = nodes.ToList();
        if (!string.IsNullOrWhiteSpace(view.Model))
            run.Model = Truncate(view.Model, AgentRunConfiguration.ModelMaxLength);
    }

    /// <summary>
    /// Stores the plan, proposal and policy snapshot the view carries (a missing one keeps the stored value), so a failed or
    /// finished run still shows which policy it was judged against.
    /// </summary>
    public static void StoreOutputs(AgentRun run, AgentWorkflowView view)
    {
        run.PlanJson = Raw(view.Plan) ?? run.PlanJson;
        run.ProposalJson = Raw(view.Proposal) ?? run.ProposalJson;
        run.PolicySnapshotJson = Raw(view.PolicySnapshot) ?? run.PolicySnapshotJson;
    }

    /// <summary>The highest validation attempt stored for the run (0 when none).</summary>
    public static async Task<int> StoredAttemptAsync(AppDbContext db, Guid runId, CancellationToken ct) =>
        await db.ValidationResults.Where(v => v.RunId == runId).MaxAsync(v => (int?)v.Attempt, ct) ?? 0;

    /// <summary>The highest validation attempt in the view (0 when none).</summary>
    public static int ViewAttempt(AgentWorkflowView view) => (view.Validation ?? []).Select(v => v.Attempt).DefaultIfEmpty(0).Max();

    [GeneratedRegex("^V(0[1-9]|1[0-2])$")]
    private static partial Regex RuleCode();

    public static string? Raw(JsonElement? element) =>
        element is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } e ? e.GetRawText() : null;

    private static string NameOrUnknown(string? name, int max) =>
        string.IsNullOrWhiteSpace(name) ? "unknown" : Truncate(name.Trim(), max);

    public static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    public static int Ms(TimeSpan span) => (int)Math.Clamp(span.TotalMilliseconds, 0, int.MaxValue);

    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>
    /// CompletedAt for a run ending now. It is never before StartedAt, because CK_AgentRuns_CompletedAt_After_StartedAt
    /// forbids that (a test clock can be behind the real clock).
    /// </summary>
    public static DateTime CompletedAt(AgentRun run, DateTime now) => run.StartedAt is { } started && now < started ? started : now;
}
