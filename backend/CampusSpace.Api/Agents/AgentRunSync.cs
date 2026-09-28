using System.Text.Json;
using System.Text.RegularExpressions;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Options;
using CampusSpace.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Agents;

/// <summary>
/// Maps the agent service's view of a run onto .NET (plan §11 step 5). Network calls happen before any transaction.
/// Every change runs in one transaction that locks the request row, then the run row, and re-checks both statuses: if
/// anything moved them since the read (a cancel, another tick), the change is skipped. The trace copy inserts only rows
/// not stored yet (steps by Sequence, rules by Attempt + RuleCode), so repeating a tick never duplicates anything.
/// </summary>
public sealed partial class AgentRunSync(
    AppDbContext db,
    IAgentClient agent,
    IAgentRunStarter starter,
    IRequestStateMachine stateMachine,
    IQuotationCalculator calculator,
    IQuotationService quotations,
    IOptions<AgentServiceOptions> options,
    TimeProvider clock,
    ILogger<AgentRunSync> logger) : IAgentRunSync
{
    public const string UnreachableMessage = "Agent service unreachable";
    public const string TimedOutMessage = "Agent run timed out";
    public const string NotFoundMessage = "Agent run not found (agent service state lost)";
    public const string NoReasonMessage = "The agent run failed without a reason";
    public const int FailureReasonMaxLength = 1000;

    public static string UnexpectedStatusMessage(string status) => $"Unexpected agent status '{status}' while the run was Running";

    /// <summary>"Agent service unreachable (last error: HTTP 401)" when the last outage is known.</summary>
    public static string WithLastError(string message, string? lastError) =>
        lastError is null ? message : $"{message} (last error: {lastError})";

    public async Task<string?> ProcessAsync(Guid runId, string? lastFailure, CancellationToken ct = default)
    {
        var run = await db.AgentRuns.AsNoTracking().Where(r => r.Id == runId)
            .Select(r => new { r.Status, r.RequestId, r.CreatedAt, r.StartedAt })
            .SingleOrDefaultAsync(ct);
        if (run is null)
            return null;
        var now = clock.GetUtcNow().UtcDateTime;
        var o = options.Value;

        if (run.Status == AgentRunStatuses.Queued)
        {
            // Watchdog: the start never got through (§10.10). CreatedAt is when submit or retry-agent queued it.
            if (now - run.CreatedAt > TimeSpan.FromMinutes(o.StartTimeoutMinutes))
            {
                await FailAsync(runId, run.RequestId, AgentRunStatuses.Queued, WithLastError(UnreachableMessage, lastFailure), null, ct);
                return null;
            }
            var start = await starter.TryStartAsync(runId, run.RequestId, timeout: null, ct);
            return start.Outcome is AgentCallOutcome.Ok or AgentCallOutcome.AlreadyExists ? null : start.Detail;
        }

        if (run.Status != AgentRunStatuses.Running)
            return null;

        var timedOut = now - (run.StartedAt ?? run.CreatedAt) > TimeSpan.FromMinutes(o.RunTimeoutMinutes);
        var result = await agent.GetAsync(runId, ct);
        switch (result.Outcome)
        {
            case AgentCallOutcome.Ok:
                await ApplyAsync(runId, run.RequestId, result.Value!, timedOut, lastFailure, ct);
                return null;
            case AgentCallOutcome.NotFound:
                await FailAsync(runId, run.RequestId, AgentRunStatuses.Running, NotFoundMessage, null, ct);
                return null;
            default:
                // Unavailable: nothing to copy. Only the watchdog acts, so a long outage can't leave the request stuck.
                var detail = result.Detail ?? result.Outcome.ToString();
                if (timedOut)
                    await FailAsync(runId, run.RequestId, AgentRunStatuses.Running, WithLastError(TimedOutMessage, detail), null, ct);
                return detail;
        }
    }

    private async Task ApplyAsync(
        Guid runId, long requestId, AgentWorkflowView view, bool timedOut, string? lastFailure, CancellationToken ct)
    {
        switch (view.Status)
        {
            case AgentWorkflowStatuses.Running when timedOut:
                await FailAsync(runId, requestId, AgentRunStatuses.Running, WithLastError(TimedOutMessage, lastFailure), view, ct);
                break;
            case AgentWorkflowStatuses.Running:
                await InLockAsync(runId, requestId, AgentRunStatuses.Running, async (_, run) => await CopyTraceAsync(run, view, ct), ct);
                break;
            case AgentWorkflowStatuses.AwaitingApproval:
                await AwaitApprovalAsync(runId, requestId, view, ct);
                break;
            case AgentWorkflowStatuses.Failed:
                await FailAsync(runId, requestId, AgentRunStatuses.Running, view.Error ?? NoReasonMessage, view, ct);
                break;
            default:
                // completed / rejected / cancelled follow an officer decision or a cancel, which .NET makes itself
                // (3.4, cancel) and never while the run is Running.
                logger.LogWarning("Agent run {RunId} reported {AgentStatus} while Running; failing it", runId, view.Status);
                await FailAsync(runId, requestId, AgentRunStatuses.Running, UnexpectedStatusMessage(view.Status), view, ct);
                break;
        }
    }

    /// <summary>
    /// The agent paused at human_gate: store the trace and the proposal, draft the quote with .NET's own calculator (the
    /// agent's numbers are never trusted; V09 compared them), and move the request to PendingApproval.
    /// </summary>
    private async Task AwaitApprovalAsync(Guid runId, long requestId, AgentWorkflowView view, CancellationToken ct)
    {
        var proposal = view.ReadProposal();
        var (quote, error) = await PriceAsync(requestId, proposal, ct);
        if (quote is null)
        {
            await FailAsync(runId, requestId, AgentRunStatuses.Running, error!, view, ct);
            return;
        }
        if (proposal!.Quote is { } agentQuote && agentQuote.Total != quote.Total)
            logger.LogWarning("Agent run {RunId}: the agent's quote total differs from the calculator's; the Draft uses the calculator's",
                runId);

        await InLockAsync(runId, requestId, AgentRunStatuses.Running, async (request, run) =>
        {
            await CopyTraceAsync(run, view, ct);
            run.Status = AgentRunStatuses.AwaitingApproval;
            run.PlanJson = Raw(view.Plan);
            run.ProposalJson = Raw(view.Proposal);
            run.PolicySnapshotJson = Raw(view.PolicySnapshot);
            run.OfficerSummary = view.OfficerSummary;
            run.DurationMs = Ms(clock.GetUtcNow().UtcDateTime - (run.StartedAt ?? run.CreatedAt));
            var quotation = await quotations.CreateDraftAsync(request.Id, quote, ct);
            quotation.AgentRunId = run.Id;
            stateMachine.Transition(request, RequestStatuses.PendingApproval, changedById: null);
        }, ct);
    }

    /// <summary>Prices the proposed room and portable equipment for the request's slot and requester role.</summary>
    private async Task<(QuoteResult? Quote, string? Error)> PriceAsync(long requestId, AgentProposal? proposal, CancellationToken ct)
    {
        if (proposal is null)
            return (null, "The agent paused for approval without a proposal");

        var request = await db.BookingRequests.AsNoTracking().Where(r => r.Id == requestId)
            .Select(r => new { r.RequestedStart, r.RequestedEnd, r.Requester.Role })
            .SingleAsync(ct);
        // room_builtin lines are qty 0 and unpriced (addendum Change B); the calculator skips qty 0 too.
        var lines = (proposal.Equipment?.Lines ?? [])
            .Where(l => l.Qty > 0 && l.Source != AgentEquipmentLine.RoomBuiltin)
            .ToList();
        var codes = lines.Select(l => l.TypeCode).Distinct().ToList();
        var typeIds = await db.EquipmentTypes.Where(t => codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var unknown = codes.Where(c => !typeIds.ContainsKey(c)).ToList();
        if (unknown.Count > 0)
            return (null, $"The proposal names unknown equipment types: {string.Join(", ", unknown)}");

        var role = request.Role == Roles.Lecturer ? RequesterRoles.Lecturer : RequesterRoles.Student;
        try
        {
            var quote = await calculator.CalculateAsync(new QuoteInput(
                proposal.RoomId, Utc(request.RequestedStart), Utc(request.RequestedEnd), role,
                lines.Select(l => new QuoteEquipmentLine(typeIds[l.TypeCode], l.Qty)).ToList()), ct);
            return quote is null
                ? (null, "Quote could not be computed: the proposed room is unknown or inactive")
                : (quote, null);
        }
        catch (BusinessRuleException ex)
        {
            return (null, $"Quote could not be computed: {ex.Message}");
        }
    }

    /// <summary>Run → Failed (with the trace, if any) and request AgentProcessing → AgentFailed, with the reason.</summary>
    private Task FailAsync(Guid runId, long requestId, string expectedStatus, string reason, AgentWorkflowView? view, CancellationToken ct) =>
        InLockAsync(runId, requestId, expectedStatus, async (request, run) =>
        {
            if (view is not null)
                await CopyTraceAsync(run, view, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            run.Status = AgentRunStatuses.Failed;
            run.FailureReason = Truncate(reason, FailureReasonMaxLength);
            if (run.StartedAt is { } started)
            {
                run.CompletedAt = now < started ? started : now;
                run.DurationMs = view?.DurationMs ?? Ms(run.CompletedAt.Value - started);
            }
            else
                run.CompletedAt = now;
            stateMachine.Transition(request, RequestStatuses.AgentFailed, changedById: null,
                Truncate(reason, RequestStatusHistoryConfiguration.ReasonMaxLength));
            logger.LogWarning("Agent run {RunId} failed: {Reason}", runId, run.FailureReason);
        }, ct);

    /// <summary>
    /// Locks the request row, then the run row (the order every writer uses), and applies <paramref name="change"/>
    /// only if the run is still <paramref name="expectedStatus"/> and the request still AgentProcessing.
    /// </summary>
    private async Task<bool> InLockAsync(
        Guid runId, long requestId, string expectedStatus, Func<BookingRequest, AgentRun, Task> change, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await RowLocks.RequestAsync(db, requestId, ct);
        var run = await RowLocks.AgentRunAsync(db, runId, ct);
        if (request is null || run is null || run.Status != expectedStatus || request.Status != RequestStatuses.AgentProcessing)
        {
            logger.LogDebug("Agent run {RunId} skipped: it or its request changed since it was read", runId);
            return false;
        }
        await change(request, run);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>Adds the steps (with their tool calls) and rule results not stored yet, and refreshes Nodes and Model.</summary>
    private async Task CopyTraceAsync(AgentRun run, AgentWorkflowView view, CancellationToken ct)
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

    [GeneratedRegex("^V(0[1-9]|1[0-2])$")]
    private static partial Regex RuleCode();

    private static string? Raw(JsonElement? element) =>
        element is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } e ? e.GetRawText() : null;

    private static string NameOrUnknown(string? name, int max) =>
        string.IsNullOrWhiteSpace(name) ? "unknown" : Truncate(name.Trim(), max);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static int Ms(TimeSpan span) => (int)Math.Clamp(span.TotalMilliseconds, 0, int.MaxValue);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
