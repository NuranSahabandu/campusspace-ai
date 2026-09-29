using System.Text.Json;
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
public sealed class AgentRunSync(
    AppDbContext db,
    IAgentClient agent,
    IAgentRunStarter starter,
    IRequestStateMachine stateMachine,
    IProposalResolver resolver,
    IQuotationService quotations,
    IApprovalFinalizer finalizer,
    IOptions<AgentServiceOptions> options,
    TimeProvider clock,
    ILogger<AgentRunSync> logger) : IAgentRunSync
{
    public const string UnreachableMessage = "Agent service unreachable";
    public const string TimedOutMessage = "Agent run timed out";
    public const string NotFoundMessage = "Agent run not found (agent service state lost)";
    public const string NoReasonMessage = "The agent run failed without a reason";
    public const int FailureReasonMaxLength = 1000;

    public const string ApprovalNotConfirmedMessage = "Agent did not confirm the approval in time";

    public static string UnexpectedStatusMessage(string status) => $"Unexpected agent status '{status}' while the run was Running";

    public static string UnexpectedResumingMessage(string status) => $"Unexpected agent status '{status}' while Resuming";

    public static string OrphanedMessage(string requestStatus) => $"Orphaned run (request is {requestStatus})";

    /// <summary>"Agent service unreachable (last error: HTTP 401)" when the last outage is known.</summary>
    public static string WithLastError(string message, string? lastError) =>
        lastError is null ? message : $"{message} (last error: {lastError})";

    public async Task<string?> ProcessAsync(Guid runId, string? lastFailure, CancellationToken ct = default)
    {
        var run = await db.AgentRuns.AsNoTracking().Where(r => r.Id == runId)
            .Select(r => new { r.Status, r.RequestId, r.CreatedAt, r.StartedAt, RequestStatus = r.Request.Status })
            .SingleOrDefaultAsync(ct);
        if (run is null)
            return null;
        var now = clock.GetUtcNow().UtcDateTime;
        var o = options.Value;
        // Written with run → Resuming in the officer's transaction, so a Resuming run always has it (unless orphaned).
        var decision = run.Status == AgentRunStatuses.Resuming
            ? await db.ApprovalDecisions.AsNoTracking().Where(d => d.AgentRunId == runId).OrderByDescending(d => d.Id)
                .Select(d => new SavedDecision(d.Decision, d.Comment, d.DecidedAt)).FirstOrDefaultAsync(ct)
            : null;

        if (!Matches(run.Status, run.RequestStatus, decision))
        {
            await OrphanAsync(runId, run.RequestId, run.Status, run.RequestStatus, ct);
            return null;
        }

        if (run.Status == AgentRunStatuses.Resuming)
            return await ResumingAsync(runId, run.RequestId, decision!, lastFailure, ct);

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
                await AwaitApprovalAsync(runId, requestId, AgentRunStatuses.Running, view, ct);
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
    /// A run after an officer's decision (approve or revise), read with the saved decision. The watchdog counts from the
    /// decision. A resume that never arrived (the agent still paused on the same validation attempt) is re-sent from the saved
    /// decision, once per tick.
    /// </summary>
    private async Task<string?> ResumingAsync(Guid runId, long requestId, SavedDecision decision, string? lastFailure, CancellationToken ct)
    {
        var approve = decision.Decision == ApprovalDecisions.Approve;
        var timedOut = clock.GetUtcNow().UtcDateTime - decision.DecidedAt > TimeSpan.FromMinutes(options.Value.RunTimeoutMinutes);
        var result = await agent.GetAsync(runId, ct);
        if (result.Outcome == AgentCallOutcome.NotFound)
        {
            logger.LogWarning("Agent run {RunId} is unknown to the agent service while Resuming", runId);
            await ResumingFailedAsync(runId, requestId, approve, NotFoundMessage, null, ct);
            return null;
        }
        if (result.Value is not { } view)
        {
            var detail = result.Detail ?? result.Outcome.ToString();
            if (timedOut)
                await ResumingTimedOutAsync(runId, requestId, approve, WithLastError(TimedOutMessage, detail), null, ct);
            return detail;
        }

        switch (view.Status)
        {
            case AgentWorkflowStatuses.Completed when approve:
                // Even after the watchdog's time: the agent confirmed, so the booking is made (or the final check fails).
                await finalizer.FinalizeApprovedAsync(runId, view, ct);
                return null;
            case AgentWorkflowStatuses.Failed:
                await ResumingFailedAsync(runId, requestId, approve, view.Error ?? NoReasonMessage, view, ct);
                return null;
            case AgentWorkflowStatuses.AwaitingApproval
                when AgentTrace.ViewAttempt(view) <= await AgentTrace.StoredAttemptAsync(db, runId, ct):
                // Still paused on the proposal the officer decided on: the resume didn't arrive.
                if (timedOut)
                    await ResumingTimedOutAsync(runId, requestId, approve, WithLastError(TimedOutMessage, lastFailure), view, ct);
                else
                    await ResendAsync(runId, decision, ct);
                return null;
            case AgentWorkflowStatuses.AwaitingApproval when !approve:
                // The revised proposal: as the first one (trace, AwaitingApproval, new Draft, PendingApproval).
                await AwaitApprovalAsync(runId, requestId, AgentRunStatuses.Resuming, view, ct);
                return null;
            case AgentWorkflowStatuses.Running when timedOut:
                await ResumingTimedOutAsync(runId, requestId, approve, WithLastError(TimedOutMessage, lastFailure), view, ct);
                return null;
            case AgentWorkflowStatuses.Running:
                await InLockAsync(runId, requestId, AgentRunStatuses.Resuming, async (_, run) => await CopyTraceAsync(run, view, ct), ct,
                    approve ? RequestStatuses.PendingApproval : RequestStatuses.AgentProcessing);
                return null;
            default:
                // completed after a revise, rejected/cancelled after either, or a new proposal after an approve: .NET never
                // asked for these.
                logger.LogWarning("Agent run {RunId} reported {AgentStatus} while Resuming for {Decision}; failing it",
                    runId, view.Status, decision.Decision);
                await ResumingFailedAsync(runId, requestId, approve, UnexpectedResumingMessage(view.Status), view, ct);
                return null;
        }
    }

    /// <summary>Approve → the new-proposal path (IApprovalFinalizer); revise → run Failed and request AgentFailed.</summary>
    private async Task ResumingFailedAsync(
        Guid runId, long requestId, bool approve, string reason, AgentWorkflowView? view, CancellationToken ct)
    {
        if (approve)
            await finalizer.FailApprovalAsync(runId, reason, view, ct);
        else
            await FailAsync(runId, requestId, AgentRunStatuses.Resuming, reason, view, ct);
    }

    private async Task ResumingTimedOutAsync(
        Guid runId, long requestId, bool approve, string reason, AgentWorkflowView? view, CancellationToken ct)
    {
        if (approve)
        {
            // Not a time failure of the booking: the proposal may still be good, so a new one is prepared.
            await finalizer.FailApprovalAsync(runId, ApprovalNotConfirmedMessage, view, ct);
            return;
        }
        await FailAsync(runId, requestId, AgentRunStatuses.Resuming, reason, view, ct);
        if (view?.Status == AgentWorkflowStatuses.AwaitingApproval)
            await CancelThreadAsync(runId, ct);
    }

    /// <summary>Sends the saved decision again (the officer's notes for a revise). A 409 means it is already being processed.</summary>
    private async Task ResendAsync(Guid runId, SavedDecision decision, CancellationToken ct)
    {
        var name = decision.Decision == ApprovalDecisions.Approve ? AgentDecisions.Approve : AgentDecisions.Revise;
        var result = await agent.ResumeAsync(runId, name, decision.Comment, ct);
        logger.LogInformation("Agent run {RunId}: re-sent the saved {Decision} decision ({Outcome})", runId, name, result.Outcome);
    }

    /// <summary>
    /// Whether a polled run and its request agree. Queued and Running runs belong to an AgentProcessing request; a Resuming
    /// run to an AgentProcessing request for a revise, or a PendingApproval one for an approve.
    /// </summary>
    private static bool Matches(string runStatus, string requestStatus, SavedDecision? decision) => runStatus switch
    {
        AgentRunStatuses.Resuming => decision?.Decision switch
        {
            ApprovalDecisions.Approve => requestStatus == RequestStatuses.PendingApproval,
            ApprovalDecisions.Revise => requestStatus == RequestStatuses.AgentProcessing,
            _ => false,
        },
        _ => requestStatus == RequestStatuses.AgentProcessing,
    };

    /// <summary>
    /// A live run whose request has moved on (manual data changes, legacy rows): the run is failed and the request left
    /// alone. A thread that may be paused gets a best-effort cancel.
    /// </summary>
    private async Task OrphanAsync(Guid runId, long requestId, string runStatus, string requestStatus, CancellationToken ct)
    {
        var failed = await InLockAsync(runId, requestId, runStatus, (_, run) =>
        {
            run.Status = AgentRunStatuses.Failed;
            run.FailureReason = OrphanedMessage(requestStatus);
            run.CompletedAt = AgentTrace.CompletedAt(run, clock.GetUtcNow().UtcDateTime);
            return Task.CompletedTask;
        }, ct, requestStatus);
        if (!failed)
            return;
        logger.LogWarning("Agent run {RunId} was {RunStatus} while its request is {RequestStatus}; failed it as orphaned",
            runId, runStatus, requestStatus);
        if (runStatus is AgentRunStatuses.Running or AgentRunStatuses.Resuming)
            await CancelThreadAsync(runId, ct);
    }

    private async Task CancelThreadAsync(Guid runId, CancellationToken ct)
    {
        var result = await agent.ResumeAsync(runId, AgentDecisions.Cancel, notes: null, ct);
        if (!result.IsOk)
            logger.LogInformation("Agent run {RunId}: the agent service did not accept the cancel ({Outcome}: {Detail})",
                runId, result.Outcome, result.Detail);
    }

    private sealed record SavedDecision(string Decision, string? Comment, DateTime DecidedAt);

    /// <summary>
    /// The agent paused at human_gate: store the trace and the proposal, draft the quote with .NET's own calculator (the
    /// agent's numbers are never trusted; V09 compared them), and move the request to PendingApproval.
    /// </summary>
    private async Task AwaitApprovalAsync(Guid runId, long requestId, string expectedStatus, AgentWorkflowView view, CancellationToken ct)
    {
        var proposal = view.ReadProposal();
        var resolved = await resolver.ResolveAsync(requestId, proposal, ct);
        if (resolved.Value is null)
        {
            await FailAsync(runId, requestId, expectedStatus, resolved.Error!, view, ct);
            return;
        }
        var quote = resolved.Value.Quote;
        if (proposal!.Quote is { } agentQuote && agentQuote.Total != quote.Total)
            logger.LogWarning("Agent run {RunId}: the agent's quote total differs from the calculator's; the Draft uses the calculator's",
                runId);

        // After a revise this overwrites the proposal and the policy snapshot with the new ones (the agent re-fetched it).
        await InLockAsync(runId, requestId, expectedStatus, async (request, run) =>
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

    /// <summary>Run → Failed (with the trace, if any) and request AgentProcessing → AgentFailed, with the reason.</summary>
    private Task FailAsync(Guid runId, long requestId, string expectedStatus, string reason, AgentWorkflowView? view, CancellationToken ct) =>
        InLockAsync(runId, requestId, expectedStatus, async (request, run) =>
        {
            if (view is not null)
            {
                await CopyTraceAsync(run, view, ct);
                // So the officer's trace shows which policy (and proposal) the failure was judged against.
                AgentTrace.StoreOutputs(run, view);
            }
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
    /// only if the run is still <paramref name="expectedStatus"/> and the request still <paramref name="requestStatus"/>
    /// (AgentProcessing unless given).
    /// </summary>
    private async Task<bool> InLockAsync(
        Guid runId, long requestId, string expectedStatus, Func<BookingRequest, AgentRun, Task> change, CancellationToken ct,
        string requestStatus = RequestStatuses.AgentProcessing)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await RowLocks.RequestAsync(db, requestId, ct);
        var run = await RowLocks.AgentRunAsync(db, runId, ct);
        if (request is null || run is null || run.Status != expectedStatus || request.Status != requestStatus)
        {
            logger.LogDebug("Agent run {RunId} skipped: it or its request changed since it was read", runId);
            return false;
        }
        await change(request, run);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private Task CopyTraceAsync(AgentRun run, AgentWorkflowView view, CancellationToken ct) =>
        AgentTrace.CopyAsync(db, run, view, logger, ct);

    private static string? Raw(JsonElement? element) => AgentTrace.Raw(element);

    private static string Truncate(string value, int max) => AgentTrace.Truncate(value, max);

    private static int Ms(TimeSpan span) => AgentTrace.Ms(span);
}
