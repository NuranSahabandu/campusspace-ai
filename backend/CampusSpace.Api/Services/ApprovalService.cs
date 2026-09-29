using System.Diagnostics;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Dtos.Requests;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Services;

public sealed class ApprovalService(
    AppDbContext db,
    ICurrentUser currentUser,
    IRequestStateMachine stateMachine,
    IApprovalFinalizer finalizer,
    IBookingRequestService requests,
    IAgentClient agent,
    IOptions<AgentServiceOptions> options,
    TimeProvider clock,
    ILogger<ApprovalService> logger) : IApprovalService
{
    public const string AlreadyDecidingMessage = "This proposal is already being decided";
    public const string ReasonRequiredMessage = "A reason is required to reject a request.";
    public const string NotesRequiredMessage = "Notes are required to request a revision.";

    public static string NotPendingMessage(string status) =>
        $"Only a request pending approval can be decided (it is {status})";

    private long CallerId => currentUser.UserId
        ?? throw new InvalidOperationException("Approval decisions need an authenticated user.");

    public async Task<ApproveResult?> ApproveAsync(long id, string? comment, CancellationToken ct = default)
    {
        var officerId = CallerId;
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        Guid runId;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (await LockPendingAsync(id, ct) is not { } locked)
                return null;
            var run = locked.Run;
            AddDecision(id, run.Id, officerId, ApprovalDecisions.Approve, comment);
            run.Status = AgentRunStatuses.Resuming;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            runId = run.Id;
        }

        // From here on the saved decision is the source of truth: if this call fails, the poller re-sends it.
        var resume = await ResumeAsync(runId, AgentDecisions.Approve, comment, ct);
        if (!resume.IsOk)
            return new ApproveResult(null, InProgress: true);

        var poll = TimeSpan.FromMilliseconds(options.Value.ApprovalPollMilliseconds);
        var wait = TimeSpan.FromSeconds(options.Value.ApprovalWaitSeconds);
        // Real time, not TimeProvider: this is how long the officer's HTTP call waits, not a business rule.
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var result = await agent.GetAsync(runId, ct);
            if (result is { IsOk: true, Value: { } view })
            {
                var outcome = view.Status switch
                {
                    AgentWorkflowStatuses.Completed => await finalizer.FinalizeApprovedAsync(runId, view, ct),
                    AgentWorkflowStatuses.Failed => await finalizer.FailApprovalAsync(runId, view.Error ?? AgentRunSync.NoReasonMessage, view, ct),
                    _ => null,
                };
                if (outcome is not null)
                    return await SettleAsync(id, runId, outcome, ct);
            }
            if (elapsed.Elapsed + poll > wait)
                return new ApproveResult(null, InProgress: true);
            await Task.Delay(poll, ct);
        }
    }

    /// <summary>The approve call's answer for a finaliser outcome. Skipped means the poller got there first.</summary>
    private async Task<ApproveResult> SettleAsync(long id, Guid runId, ApprovalOutcome outcome, CancellationToken ct)
    {
        if (outcome.IsFailure)
            throw new ConflictException(outcome.Message!);
        if (outcome.Kind == ApprovalOutcomeKind.Skipped)
        {
            var run = await db.AgentRuns.AsNoTracking().Where(r => r.Id == runId)
                .Select(r => new { r.Status, r.FailureReason, RequestStatus = r.Request.Status }).SingleAsync(ct);
            if (run.Status == AgentRunStatuses.Failed)
                throw new ConflictException(run.RequestStatus == RequestStatuses.Rejected
                    ? ApprovalFinalizer.TimeClosedMessageFromReason(await ClosedReasonAsync(id, ct))
                    : ApprovalFinalizer.NewProposalMessage(run.FailureReason!));
            if (run.Status != AgentRunStatuses.Completed)
                return new ApproveResult(null, InProgress: true);
        }
        return new ApproveResult(await requests.GetAsync(id, ct), InProgress: false);
    }

    /// <summary>The time close's history reason: the run's FailureReason is the original (often the agent's) reason.</summary>
    private Task<string> ClosedReasonAsync(long id, CancellationToken ct) =>
        db.RequestStatusHistory.AsNoTracking().Where(h => h.RequestId == id && h.ToStatus == RequestStatuses.Rejected)
            .OrderByDescending(h => h.Id).Select(h => h.Reason!).FirstAsync(ct);

    public async Task<BookingRequestDetailDto?> RejectAsync(long id, string? reason, CancellationToken ct = default)
    {
        var officerId = CallerId;
        // Officer text: stored as given (trimmed), never interpreted.
        var text = string.IsNullOrWhiteSpace(reason)
            ? throw new BusinessRuleException(nameof(RejectRequest.Reason), ReasonRequiredMessage)
            : reason.Trim();

        Guid runId;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (await LockPendingAsync(id, ct) is not { } locked)
                return null;
            var (request, run) = locked;
            AddDecision(id, run.Id, officerId, ApprovalDecisions.Reject, text);
            run.Status = AgentRunStatuses.Rejected;
            run.CompletedAt = AgentTrace.CompletedAt(run, clock.GetUtcNow().UtcDateTime);
            await QuotationService.VoidLiveAsync(db, id, ct);
            stateMachine.Transition(request, RequestStatuses.Rejected, officerId, HistoryReason(text));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            runId = run.Id;
        }

        // Ends the paused thread. The .NET run is already Rejected and never polled again, so a failure is only logged.
        await ResumeAsync(runId, AgentDecisions.Reject, text, ct);
        return await requests.GetAsync(id, ct);
    }

    public async Task<BookingRequestDetailDto?> RequestRevisionAsync(long id, string? notes, CancellationToken ct = default)
    {
        var officerId = CallerId;
        var text = string.IsNullOrWhiteSpace(notes)
            ? throw new BusinessRuleException(nameof(RevisionRequest.Notes), NotesRequiredMessage)
            : notes.Trim();

        Guid runId;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (await LockPendingAsync(id, ct) is not { } locked)
                return null;
            var (request, run) = locked;
            AddDecision(id, run.Id, officerId, ApprovalDecisions.Revise, text);
            // The same LangGraph thread and the same row continue; RevisionNo is .NET's counter per request.
            run.RevisionNo = await db.AgentRuns.Where(r => r.RequestId == id).MaxAsync(r => r.RevisionNo, ct) + 1;
            run.Status = AgentRunStatuses.Resuming;
            await QuotationService.VoidLiveAsync(db, id, ct);
            stateMachine.Transition(request, RequestStatuses.RevisionRequested, officerId, HistoryReason(text));
            stateMachine.Transition(request, RequestStatuses.AgentProcessing, officerId, HistoryReason(text));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            runId = run.Id;
        }

        await ResumeAsync(runId, AgentDecisions.Revise, text, ct);
        return await requests.GetAsync(id, ct);
    }

    /// <summary>
    /// Locks the request row, then its live run row (the order every writer uses). Null when the request doesn't exist; a
    /// ConflictException unless the request is PendingApproval and its run AwaitingApproval.
    /// </summary>
    private async Task<(BookingRequest Request, AgentRun Run)?> LockPendingAsync(long id, CancellationToken ct)
    {
        var request = await RowLocks.RequestAsync(db, id, ct);
        if (request is null)
            return null;
        if (request.Status != RequestStatuses.PendingApproval)
            throw new ConflictException(NotPendingMessage(request.Status));
        var run = await RowLocks.LiveAgentRunAsync(db, id, ct);
        if (run is not { Status: AgentRunStatuses.AwaitingApproval })
            throw new ConflictException(run?.Status == AgentRunStatuses.Resuming
                ? AlreadyDecidingMessage
                : NotPendingMessage(request.Status));
        return (request, run);
    }

    private void AddDecision(long requestId, Guid runId, long officerId, string decision, string? comment) =>
        db.ApprovalDecisions.Add(new ApprovalDecision
        {
            RequestId = requestId,
            AgentRunId = runId,
            OfficerId = officerId,
            Decision = decision,
            Comment = comment,
            DecidedAt = clock.GetUtcNow().UtcDateTime,
        });

    /// <summary>Best-effort resume, capped at AgentService:InlineStartTimeoutSeconds. Never throws for an agent-side answer.</summary>
    private async Task<AgentCallResult<AgentWorkflowAccepted>> ResumeAsync(Guid runId, string decision, string? notes, CancellationToken ct)
    {
        AgentCallResult<AgentWorkflowAccepted> result;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limit.CancelAfter(TimeSpan.FromSeconds(options.Value.InlineStartTimeoutSeconds));
            try
            {
                result = await agent.ResumeAsync(runId, decision, notes, limit.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result = AgentCallResult<AgentWorkflowAccepted>.Unavailable("timeout");
            }
        }
        if (!result.IsOk)
            logger.LogWarning("Agent run {RunId}: resume {Decision} was not accepted ({Outcome}: {Detail})",
                runId, decision, result.Outcome, result.Detail);
        return result;
    }

    private static string HistoryReason(string text) => AgentTrace.Truncate(text, RequestStatusHistoryConfiguration.ReasonMaxLength);
}
