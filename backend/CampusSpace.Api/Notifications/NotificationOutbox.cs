using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Notifications;

/// <summary>
/// The outbox trigger (plan §14, Task 5.2). AppDbContext.SaveChanges calls <see cref="EnqueueAsync"/> before it saves:
/// every new RequestStatusHistory row that deserves an email gets a Pending NotificationLog in the same SaveChanges, so the
/// email row commits or rolls back with the status change. Every status change goes through IRequestStateMachine on a
/// tracked request, so every path (approve, the poller, FailApprovalAsync, reject, revise, cancel) is covered.
/// </summary>
public static class NotificationOutbox
{
    /// <summary>
    /// The email kind for a status change, or null for none. Rejected: the time close's fixed reason marker
    /// (<see cref="ApprovalFinalizer.TimeClosedPrefix"/>) means Closed; otherwise an officer's rejection is Rejected, and a
    /// system rejection without the marker (no such path exists today) is Closed too, so a system reason is never sent.
    /// </summary>
    public static string? KindFor(RequestStatusHistory change, bool cancelledByOfficer) => change.ToStatus switch
    {
        RequestStatuses.Approved => NotificationKinds.Approved,
        RequestStatuses.Rejected when IsTimeClose(change.Reason) => NotificationKinds.Closed,
        RequestStatuses.Rejected when change.ChangedById is not null => NotificationKinds.Rejected,
        RequestStatuses.Rejected => NotificationKinds.Closed,
        RequestStatuses.RevisionRequested => NotificationKinds.RevisionRequested,
        RequestStatuses.Cancelled when cancelledByOfficer => NotificationKinds.CancelledByOfficer,
        _ => null,
    };

    public static bool IsTimeClose(string? reason) =>
        reason?.StartsWith(ApprovalFinalizer.TimeClosedPrefix, StringComparison.Ordinal) == true;

    internal static async Task EnqueueAsync(DbContext db, bool async, CancellationToken ct)
    {
        var tracker = db.ChangeTracker;
        var queued = tracker.Entries<NotificationLog>().Select(e => e.Entity.StatusHistory).ToHashSet();
        var changes = new List<(BookingRequest Request, RequestStatusHistory Change, string Kind)>();
        foreach (var request in tracker.Entries<BookingRequest>().Select(e => e.Entity))
        {
            foreach (var change in request.StatusHistory)
            {
                if (queued.Contains(change) || db.Entry(change).State != EntityState.Added)
                    continue;
                if (KindFor(change, request.CancelledByOfficer) is { } kind)
                    changes.Add((request, change, kind));
            }
        }
        if (changes.Count == 0)
            return;

        // A requester that is loaded or tracked (possibly new, in this same save) has no key to look up yet.
        var requesterIds = changes.Where(c => c.Request.Requester is null).Select(c => c.Request.RequesterId).Distinct().ToList();
        var query = db.Set<User>().AsNoTracking().Where(u => requesterIds.Contains(u.Id)).Select(u => new { u.Id, u.Email });
        var emails = requesterIds.Count == 0
            ? []
            : (async ? await query.ToListAsync(ct) : query.ToList()).ToDictionary(u => u.Id, u => u.Email);

        foreach (var (request, change, kind) in changes)
        {
            db.Set<NotificationLog>().Add(new NotificationLog
            {
                Request = request,
                StatusHistory = change,
                Kind = kind,
                Channel = NotificationChannels.Email,
                Recipient = request.Requester?.Email ?? emails[request.RequesterId],
                Status = NotificationStatuses.Pending,
            });
        }
    }
}
