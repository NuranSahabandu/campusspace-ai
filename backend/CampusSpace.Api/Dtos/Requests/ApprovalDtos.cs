using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Data.Configurations;

namespace CampusSpace.Api.Dtos.Requests;

/// <summary>POST /api/booking-requests/{id}/approve. The comment is optional officer text; the body may be omitted.</summary>
public record ApproveRequest([MaxLength(ApprovalDecisionConfiguration.CommentMaxLength)] string? Comment);

/// <summary>POST /api/booking-requests/{id}/reject. The reason is required (UC20) and shown to the requester in the history.</summary>
public record RejectRequest([Required, MaxLength(ApprovalDecisionConfiguration.CommentMaxLength)] string? Reason);

/// <summary>
/// POST /api/booking-requests/{id}/request-revision. The notes are required; they reach the agent Supervisor as the re-plan
/// reason (UC21).
/// </summary>
public record RevisionRequest([Required, MaxLength(ApprovalDecisionConfiguration.CommentMaxLength)] string? Notes);

/// <summary>
/// The 202 body of approve when the agent hasn't confirmed within AgentService:ApprovalWaitSeconds: the decision is saved
/// and the poller finishes the approval. Poll the request (Location) until it leaves PendingApproval.
/// </summary>
public record ApprovalInProgressDto(long RequestId, string Status)
{
    public const string InProgress = "ApprovalInProgress";
}
