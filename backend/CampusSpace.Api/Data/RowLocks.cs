using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Data;

/// <summary>
/// Row locks (SELECT … FOR UPDATE) in the caller's transaction; the rows come back tracked. Lock order everywhere:
/// the requester's advisory lock (submit and retry-agent only), then the booking request row, then its agent run row,
/// then any booking and item rows. Every writer that changes a request or its run takes them in this order, so they
/// serialise instead of deadlocking.
/// </summary>
public static class RowLocks
{
    public static Task<BookingRequest?> RequestAsync(AppDbContext db, long requestId, CancellationToken ct) =>
        db.BookingRequests
            .FromSql($"""SELECT * FROM "BookingRequests" WHERE "Id" = {requestId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);

    public static Task<AgentRun?> AgentRunAsync(AppDbContext db, Guid runId, CancellationToken ct) =>
        db.AgentRuns
            .FromSql($"""SELECT * FROM "AgentRuns" WHERE "Id" = {runId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);

    /// <summary>The request's live run (AgentRunStatuses.Active; at most one, IX_AgentRuns_RequestId_Live), locked.</summary>
    public static Task<AgentRun?> LiveAgentRunAsync(AppDbContext db, long requestId, CancellationToken ct)
    {
        var active = AgentRunStatuses.Active.ToArray();
        return db.AgentRuns
            .FromSql($"""SELECT * FROM "AgentRuns" WHERE "RequestId" = {requestId} AND "Status" = ANY({active}) FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
    }
}
