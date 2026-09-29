using CampusSpace.Api.Agents;

namespace CampusSpace.Api.Services;

/// <summary>
/// How an approval failure is handled (addendum Open question 7, decided for 3.4, refined in 3.6). Every failure, whatever its
/// source, is classified in <see cref="IApprovalFinalizer.FailApprovalAsync"/> by .NET's own time checks first. Time: V05
/// (current policy), V06 as of submission, or a start in the past; the time comes from the requester's form, so a re-plan
/// can't fix it and the request is closed with .NET's message. Proposal: the time is still valid (room busy or 23P01,
/// blackout, room inactive, equipment short, builtin feature removed, agent finalize failed, not confirmed in time); a new
/// proposal is prepared.
/// </summary>
public enum ApprovalFailureKind
{
    Time,
    Proposal,
}

public enum ApprovalOutcomeKind
{
    /// <summary>Booked: request Approved, run Completed.</summary>
    Approved,
    /// <summary>Nothing to do: the run is no longer being approved (already finished, cancelled, or another caller won).</summary>
    Skipped,
    /// <summary>A time failure: run Failed, request Rejected by the system, no new run.</summary>
    TimeClosed,
    /// <summary>A proposal failure: run Failed, request RevisionRequested → AgentProcessing with a new run.</summary>
    NewProposal,
}

/// <summary><paramref name="Message"/> is the officer's 409 title for TimeClosed and NewProposal.</summary>
public sealed record ApprovalOutcome(ApprovalOutcomeKind Kind, string? Message = null)
{
    public static readonly ApprovalOutcome Approved = new(ApprovalOutcomeKind.Approved);
    public static readonly ApprovalOutcome Skipped = new(ApprovalOutcomeKind.Skipped);

    public bool IsFailure => Kind is ApprovalOutcomeKind.TimeClosed or ApprovalOutcomeKind.NewProposal;
}

/// <summary>
/// Finishes an officer's approval (plan App. A.3, §11 step 7). Shared by the approve endpoint and the poller, so either can
/// finish it, and idempotent: each method locks the request row, then the run row, and acts only while the run is Resuming
/// for an Approve decision and the request is PendingApproval.
/// </summary>
public interface IApprovalFinalizer
{
    /// <summary>
    /// The approval transaction (READ COMMITTED), after the agent's finalize reported completed: re-checks V05 and V06
    /// (current policy, V06 as of submission) and the start, the room (active, no blackout, not booked), the builtin lines,
    /// then inserts the Booking (the exclusion constraint is the guarantee), reserves the equipment, issues the quote
    /// (recomputed by IQuotationCalculator), and moves the request to Approved and the run to Completed. A failed check is
    /// rolled back and handed to <see cref="FailApprovalAsync"/>, which classifies it.
    /// </summary>
    Task<ApprovalOutcome> FinalizeApprovedAsync(Guid runId, AgentWorkflowView view, CancellationToken ct = default);

    /// <summary>
    /// The one place an approval failure is classified (<see cref="ApprovalFailureKind"/>). In one transaction: run → Failed
    /// with <paramref name="reason"/> (kept for the trace), the live quote voided, then .NET's time checks. Time invalid →
    /// request Rejected by the system with .NET's time message, no new run, then a best-effort cancel. Otherwise → request
    /// PendingApproval → RevisionRequested → AgentProcessing with a new run (RevisionNo = next), then a best-effort start.
    /// </summary>
    Task<ApprovalOutcome> FailApprovalAsync(Guid runId, string reason, AgentWorkflowView? view, CancellationToken ct = default);
}
