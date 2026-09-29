using CampusSpace.Api.Agents;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Approvals;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class ApprovalQueueService(AppDbContext db) : IApprovalQueueService
{
    public async Task<PagedResult<ApprovalQueueItemDto>> ListAsync(ApprovalQueueQuery query, CancellationToken ct = default)
    {
        var requests = db.BookingRequests.AsNoTracking().Where(r => r.Status == RequestStatuses.PendingApproval);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = query.Search.ToContainsPattern();
            requests = requests.Where(r =>
                EF.Functions.ILike(r.Purpose, pattern)
                || EF.Functions.ILike(r.Requester.FullName, pattern)
                || EF.Functions.ILike(r.Requester.Email, pattern)
                || (r.Club != null && EF.Functions.ILike(r.Club.Name, pattern)));
        }

        var rows = requests.Select(r => new QueueRow
        {
            Id = r.Id,
            Purpose = r.Purpose,
            RequesterName = r.Requester.FullName,
            RequesterRole = r.Requester.Role,
            ClubName = r.Club != null ? r.Club.Name : null,
            Start = r.RequestedStart,
            End = r.RequestedEnd,
            Attendees = r.Attendees,
            // A PendingApproval request always has this history row; UpdatedAt only guards hand-made data.
            PendingSince = r.StatusHistory.Where(h => h.ToStatus == RequestStatuses.PendingApproval)
                .Max(h => (DateTime?)h.ChangedAt) ?? r.UpdatedAt,
            DraftTotal = db.Quotations.Where(q => q.RequestId == r.Id && QuotationStatuses.Live.Contains(q.Status))
                .OrderByDescending(q => q.Id).Select(q => (decimal?)q.Total).FirstOrDefault(),
            Exempt = db.Quotations.Where(q => q.RequestId == r.Id && QuotationStatuses.Live.Contains(q.Status))
                .OrderByDescending(q => q.Id).Select(q => q.IsExempt).FirstOrDefault(),
            RevisionNo = db.AgentRuns.Where(a => a.RequestId == r.Id && AgentRunStatuses.Active.Contains(a.Status))
                .Select(a => (int?)a.RevisionNo).FirstOrDefault(),
            ProposalJson = db.AgentRuns.Where(a => a.RequestId == r.Id && AgentRunStatuses.Active.Contains(a.Status))
                .Select(a => a.ProposalJson).FirstOrDefault(),
        });

        rows = query.Sort switch
        {
            "-pendingSince" => rows.OrderByDescending(x => x.PendingSince).ThenByDescending(x => x.Id),
            "requestedStart" => rows.OrderBy(x => x.Start).ThenBy(x => x.Id),
            "-requestedStart" => rows.OrderByDescending(x => x.Start).ThenByDescending(x => x.Id),
            "attendees" => rows.OrderBy(x => x.Attendees).ThenBy(x => x.Id),
            "-attendees" => rows.OrderByDescending(x => x.Attendees).ThenByDescending(x => x.Id),
            _ => rows.OrderBy(x => x.PendingSince).ThenBy(x => x.Id),
        };

        var page = await rows.ToPagedResultAsync(query, ct);

        // jsonb isn't queryable through LINQ here, so room_id is read for this page only; the code comes from Rooms.
        var roomIds = page.Items.Select(x => ProposalMapper.RoomId(x.ProposalJson)).ToList();
        var wanted = roomIds.OfType<long>().Distinct().ToList();
        var codes = await db.Rooms.AsNoTracking().Where(r => wanted.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, ct);

        var items = page.Items.Select((x, i) => new ApprovalQueueItemDto(
            x.Id, x.Purpose, x.RequesterName, x.RequesterRole, x.ClubName, x.Start, x.End, x.Attendees,
            roomIds[i] is { } roomId ? codes.GetValueOrDefault(roomId) : null,
            x.DraftTotal, x.Exempt, x.RevisionNo, x.PendingSince)).ToList();
        return new PagedResult<ApprovalQueueItemDto>(items, page.Page, page.PageSize, page.Total);
    }

    private sealed class QueueRow
    {
        public long Id { get; init; }
        public string Purpose { get; init; } = "";
        public string RequesterName { get; init; } = "";
        public string RequesterRole { get; init; } = "";
        public string? ClubName { get; init; }
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public int Attendees { get; init; }
        public DateTime PendingSince { get; init; }
        public decimal? DraftTotal { get; init; }
        public bool Exempt { get; init; }
        public int? RevisionNo { get; init; }
        public string? ProposalJson { get; init; }
    }
}
