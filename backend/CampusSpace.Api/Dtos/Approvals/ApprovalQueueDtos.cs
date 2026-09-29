using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Approvals;

/// <summary>
/// GET /api/approvals/queue (Facilities Officer). Search matches the purpose, the requester's name or email and the club
/// name. Oldest pending first by default (pendingSince ascending).
/// </summary>
public record ApprovalQueueQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["pendingSince", "requestedStart", "attendees"];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}

/// <summary>
/// One request waiting for the officer. Times are UTC. PendingSince is when it last entered PendingApproval.
/// DraftTotal and Exempt come from .NET's live quote (null if it has none); ProposedRoomCode and RevisionNo from the live
/// agent run.
/// </summary>
public record ApprovalQueueItemDto(
    long RequestId, string Purpose, string RequesterName, string RequesterRole, string? ClubName,
    DateTime Start, DateTime End, int Attendees, string? ProposedRoomCode, decimal? DraftTotal, bool Exempt,
    int? RevisionNo, DateTime PendingSince);
