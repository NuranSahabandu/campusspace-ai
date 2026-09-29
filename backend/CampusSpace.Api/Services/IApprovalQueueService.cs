using CampusSpace.Api.Dtos.Approvals;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Services;

/// <summary>The Facilities Officer's approval queue (UC17): requests in PendingApproval. Read-only.</summary>
public interface IApprovalQueueService
{
    Task<PagedResult<ApprovalQueueItemDto>> ListAsync(ApprovalQueueQuery query, CancellationToken ct = default);
}
