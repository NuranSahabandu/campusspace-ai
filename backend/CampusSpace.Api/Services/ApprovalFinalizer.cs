using System.Data;
using System.Globalization;
using System.Text.Json;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CampusSpace.Api.Services;

public sealed class ApprovalFinalizer(
    AppDbContext db,
    IProposalResolver resolver,
    IPolicySettingsService policy,
    IBookingWindowRules windowRules,
    IEquipmentAvailabilityService equipment,
    IQuotationService quotations,
    IRequestStateMachine stateMachine,
    IAgentRunStarter starter,
    IAgentClient agent,
    IOptions<AgentServiceOptions> options,
    TimeProvider clock,
    ILogger<ApprovalFinalizer> logger) : IApprovalFinalizer
{
    public static string NewProposalMessage(string reason) =>
        $"The proposal is no longer valid: {Sentence(reason)}. A new proposal is being prepared.";

    public static string TimeClosedMessage(string reason) =>
        $"The requested time is no longer valid: {Sentence(reason)}. The request was closed; the requester can submit a new time.";

    /// <summary>The history reason when a time failure closes the request.</summary>
    public static string TimeClosedReason(string reason) => $"The requested time is no longer valid: {Sentence(reason)}";

    public async Task<ApprovalOutcome> FinalizeApprovedAsync(Guid runId, AgentWorkflowView view, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        ApprovalFailure failure;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            var (request, run) = await LockAsync(runId, ct);
            if (request is null || run is null || !await IsApprovingAsync(request, run, ct))
                return ApprovalOutcome.Skipped;
            var decision = await LatestDecisionAsync(runId, ct);

            try
            {
                var found = await CheckTimeAsync(request, ct)
                    ?? await ResolveAndBookAsync(request, run, decision!, view, ct);
                if (found is null)
                {
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    logger.LogInformation("Agent run {RunId} approved: request {RequestId} booked", runId, request.Id);
                    return ApprovalOutcome.Approved;
                }
                failure = found;
            }
            catch (ConflictException ex)
            {
                // ReserveAsync: "Not enough equipment: …" (V08).
                failure = new(ApprovalFailureKind.Proposal, ex.Message);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            {
                SqlState: GlobalExceptionHandler.ExclusionViolation, ConstraintName: BookingConfiguration.NoRoomOverlapConstraint,
            })
            {
                // Another approval committed an overlapping booking after the friendly check: the constraint is the guarantee.
                failure = new(ApprovalFailureKind.Proposal, await RoomBookedMessageAsync(view, ct));
            }
            await transaction.RollbackAsync(ct);
        }

        db.ChangeTracker.Clear();
        return failure.Kind == ApprovalFailureKind.Time
            ? await CloseForTimeAsync(runId, failure.Reason, view, ct)
            : await FailApprovalAsync(runId, failure.Reason, view, ct);
    }

    /// <summary>V05 and V06 with the current policy; lead time and advance window as of submission (see CLAUDE.md).</summary>
    private async Task<ApprovalFailure?> CheckTimeAsync(BookingRequest request, CancellationToken ct)
    {
        var start = AgentTrace.Utc(request.RequestedStart);
        var end = AgentTrace.Utc(request.RequestedEnd);
        var snapshot = await policy.GetAsync(ct);

        var slot = windowRules.CheckSlot(start, end, snapshot);
        if (!slot.IsValid)
            return new(ApprovalFailureKind.Time, Join(slot));
        if (start <= clock.GetUtcNow())
            return new(ApprovalFailureKind.Time, Sentence(BookingWindowRules.FutureMessage));

        var submittedAt = await db.RequestStatusHistory
            .Where(h => h.RequestId == request.Id && h.ToStatus == RequestStatuses.Submitted)
            .OrderBy(h => h.ChangedAt).Select(h => (DateTime?)h.ChangedAt)
            .FirstOrDefaultAsync(ct) ?? request.CreatedAt;
        var role = await db.Users.Where(u => u.Id == request.RequesterId).Select(u => u.Role).SingleAsync(ct);
        var timing = windowRules.CheckTiming(start, role, snapshot, AgentTrace.Utc(submittedAt));
        return timing.IsValid ? null : new(ApprovalFailureKind.Time, $"{Sentence(Join(timing))} (as of submission)");
    }

    /// <summary>
    /// The room and equipment re-checks (V02, V07, V08 and the builtin lines of addendum B), then the booking, its reservations,
    /// the issued quote and the status changes, all tracked for the caller's SaveChanges. Null when everything is in place.
    /// </summary>
    private async Task<ApprovalFailure?> ResolveAndBookAsync(
        BookingRequest request, AgentRun run, ApprovalDecision decision, AgentWorkflowView view, CancellationToken ct)
    {
        var proposal = view.ReadProposal() ?? ReadStored(run.ProposalJson);
        var resolved = await resolver.ResolveAsync(request.Id, proposal, ct);
        if (resolved.Value is not { } p)
            return new(ApprovalFailureKind.Proposal, resolved.Error!);

        var room = await db.Rooms.AsNoTracking().Where(r => r.Id == p.RoomId)
            .Select(r => new { r.Code, r.IsActive, Features = r.RoomFeatures.Select(f => f.Feature.Code).ToList() })
            .SingleOrDefaultAsync(ct);
        if (room is null || !room.IsActive)
            return new(ApprovalFailureKind.Proposal, $"Room {room?.Code ?? p.RoomId.ToString(CultureInfo.InvariantCulture)} is no longer active");
        var window = CampusTime.UtcRange(p.Start, p.End);
        if (await db.RoomBlackouts.AnyAsync(b => b.RoomId == p.RoomId && b.TimeRange.Overlaps(window), ct))
            return new(ApprovalFailureKind.Proposal, $"Room {room.Code} is blacked out for this time");
        if (await db.Bookings.AnyAsync(b => b.RoomId == p.RoomId && BookingStatuses.Active.Contains(b.Status)
                && b.TimeRange.Overlaps(window), ct))
            return new(ApprovalFailureKind.Proposal, RoomBookedMessage(room.Code));
        foreach (var line in p.BuiltinLines)
        {
            if (line.CoveredByFeatureCode is null)
                return new(ApprovalFailureKind.Proposal, $"{line.TypeCode} is not covered by any room feature");
            if (!room.Features.Contains(line.CoveredByFeatureCode))
                return new(ApprovalFailureKind.Proposal,
                    $"Room {room.Code} no longer has the {line.CoveredByFeatureCode} feature that covers {line.TypeCode}");
        }

        var booking = new Booking { Request = request, RoomId = p.RoomId, TimeRange = window, Status = BookingStatuses.Confirmed };
        db.Bookings.Add(booking);
        await equipment.ReserveAsync(booking, p.PortableLines, ct);

        var issued = await quotations.IssueForApprovalAsync(request.Id, run.Id, p.Quote, ct);
        string? reason = null;
        if (issued.Recalculated && issued.DraftTotal is { } draftTotal && draftTotal != p.Quote.Total)
        {
            reason = $"Quote recalculated at approval: Draft {Money(draftTotal)}, issued {Money(p.Quote.Total)}";
            logger.LogWarning("Agent run {RunId}: the quote changed between the proposal and the approval", run.Id);
        }
        stateMachine.Transition(request, RequestStatuses.Approved, decision.OfficerId, reason);

        await AgentTrace.CopyAsync(db, run, view, logger, ct);
        AgentTrace.StoreOutputs(run, view);
        var now = clock.GetUtcNow().UtcDateTime;
        run.Status = AgentRunStatuses.Completed;
        run.CompletedAt = AgentTrace.CompletedAt(run, now);
        run.DurationMs = view.DurationMs ?? AgentTrace.Ms(run.CompletedAt.Value - (run.StartedAt ?? run.CreatedAt));
        return null;
    }

    public async Task<ApprovalOutcome> FailApprovalAsync(Guid runId, string reason, AgentWorkflowView? view, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        long requestId;
        Guid nextRunId;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var (request, run) = await LockAsync(runId, ct);
            if (request is null || run is null || !await IsApprovingAsync(request, run, ct))
                return ApprovalOutcome.Skipped;

            await EndRunAsync(run, reason, view, ct);
            await QuotationService.VoidLiveAsync(db, request.Id, ct);
            stateMachine.Transition(request, RequestStatuses.RevisionRequested, changedById: null, HistoryReason(reason));
            // Saved first: the failed run must leave IX_AgentRuns_RequestId_Live before the new run enters it.
            await db.SaveChangesAsync(ct);

            var next = await starter.AddRunAsync(request, ct);
            stateMachine.Transition(request, RequestStatuses.AgentProcessing, changedById: null);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            (requestId, nextRunId) = (request.Id, next.Id);
        }
        logger.LogWarning("Agent run {RunId}: the approval failed ({Reason}); new run {NextRunId} prepares a new proposal",
            runId, reason, nextRunId);

        await starter.TryStartAsync(nextRunId, requestId, TimeSpan.FromSeconds(options.Value.InlineStartTimeoutSeconds), ct);
        // The old thread may still be paused (a lost resume or a watchdog failure); a finished one answers 409.
        if (view is null || view.Status == AgentWorkflowStatuses.AwaitingApproval)
            await CancelThreadAsync(runId, ct);
        return new(ApprovalOutcomeKind.NewProposal, NewProposalMessage(reason));
    }

    /// <summary>
    /// A time failure (V05, V06 or a start in the past): a re-plan can't fix the time the requester chose, so the request is
    /// closed as Rejected by the system (addendum Open question 7, see CLAUDE.md). No new run.
    /// </summary>
    private async Task<ApprovalOutcome> CloseForTimeAsync(Guid runId, string reason, AgentWorkflowView? view, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var (request, run) = await LockAsync(runId, ct);
            if (request is null || run is null || !await IsApprovingAsync(request, run, ct))
                return ApprovalOutcome.Skipped;

            await EndRunAsync(run, reason, view, ct);
            await QuotationService.VoidLiveAsync(db, request.Id, ct);
            stateMachine.Transition(request, RequestStatuses.Rejected, changedById: null, HistoryReason(TimeClosedReason(reason)));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        logger.LogWarning("Agent run {RunId}: the requested time is no longer valid ({Reason}); the request was closed", runId, reason);

        await CancelThreadAsync(runId, ct);
        return new(ApprovalOutcomeKind.TimeClosed, TimeClosedMessage(reason));
    }

    /// <summary>Run → Failed with the reason, the trace and outputs (so the officer sees which policy it was judged against).</summary>
    private async Task EndRunAsync(AgentRun run, string reason, AgentWorkflowView? view, CancellationToken ct)
    {
        if (view is not null)
        {
            await AgentTrace.CopyAsync(db, run, view, logger, ct);
            AgentTrace.StoreOutputs(run, view);
        }
        run.Status = AgentRunStatuses.Failed;
        run.FailureReason = AgentTrace.Truncate(reason, AgentRunSync.FailureReasonMaxLength);
        run.CompletedAt = AgentTrace.CompletedAt(run, clock.GetUtcNow().UtcDateTime);
        if (run.StartedAt is { } started)
            run.DurationMs ??= AgentTrace.Ms(run.CompletedAt.Value - started);
    }

    private async Task CancelThreadAsync(Guid runId, CancellationToken ct)
    {
        var result = await agent.ResumeAsync(runId, AgentDecisions.Cancel, notes: null, ct);
        if (!result.IsOk)
            logger.LogInformation("Agent run {RunId}: the agent service did not accept the cancel ({Outcome}: {Detail})",
                runId, result.Outcome, result.Detail);
    }

    /// <summary>Locks the run's request row, then the run row (the order every writer uses).</summary>
    private async Task<(BookingRequest? Request, AgentRun? Run)> LockAsync(Guid runId, CancellationToken ct)
    {
        var requestId = await db.AgentRuns.Where(r => r.Id == runId).Select(r => (long?)r.RequestId).SingleOrDefaultAsync(ct);
        if (requestId is not { } id)
            return (null, null);
        var request = await RowLocks.RequestAsync(db, id, ct);
        var run = await RowLocks.AgentRunAsync(db, runId, ct);
        return (request, run);
    }

    /// <summary>The state an approval is finished from: run Resuming for an Approve decision, request still PendingApproval.</summary>
    private async Task<bool> IsApprovingAsync(BookingRequest request, AgentRun run, CancellationToken ct)
    {
        if (run.Status != AgentRunStatuses.Resuming || request.Status != RequestStatuses.PendingApproval)
        {
            logger.LogDebug("Agent run {RunId} skipped: it or its request is no longer being approved", run.Id);
            return false;
        }
        return (await LatestDecisionAsync(run.Id, ct))?.Decision == ApprovalDecisions.Approve;
    }

    private Task<ApprovalDecision?> LatestDecisionAsync(Guid runId, CancellationToken ct) =>
        db.ApprovalDecisions.AsNoTracking().Where(d => d.AgentRunId == runId).OrderByDescending(d => d.Id).FirstOrDefaultAsync(ct);

    private async Task<string> RoomBookedMessageAsync(AgentWorkflowView view, CancellationToken ct)
    {
        var roomId = view.ReadProposal()?.RoomId;
        var code = await db.Rooms.Where(r => r.Id == roomId).Select(r => r.Code).SingleOrDefaultAsync(ct);
        return RoomBookedMessage(code ?? "the proposed room");
    }

    private static string RoomBookedMessage(string code) => $"Room {code} was just booked for this time";

    private static AgentProposal? ReadStored(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<AgentProposal>(json, AgentJson.Options);

    private static string Join(BookingWindowErrors errors) =>
        string.Join("; ", new[] { errors.Start, errors.End }.Where(m => m is not null).Select(m => Sentence(m!)));

    /// <summary>A reason without its closing full stop, so it reads well inside a longer message.</summary>
    private static string Sentence(string reason) => reason.TrimEnd().TrimEnd('.');

    private static string HistoryReason(string reason) => AgentTrace.Truncate(reason, RequestStatusHistoryConfiguration.ReasonMaxLength);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private sealed record ApprovalFailure(ApprovalFailureKind Kind, string Reason);
}
