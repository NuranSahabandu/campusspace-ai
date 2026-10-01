using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Notifications;

public interface INotificationLogService
{
    /// <summary>The request's emails, newest first; null when the request doesn't exist.</summary>
    Task<IReadOnlyList<NotificationLogDto>?> ListForRequestAsync(long requestId, CancellationToken ct = default);
}

public sealed class NotificationLogService(AppDbContext db) : INotificationLogService
{
    public async Task<IReadOnlyList<NotificationLogDto>?> ListForRequestAsync(long requestId, CancellationToken ct = default)
    {
        if (!await db.BookingRequests.AnyAsync(r => r.Id == requestId, ct))
            return null;

        return await db.NotificationLogs.AsNoTracking().Where(n => n.RequestId == requestId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Select(n => new NotificationLogDto(
                n.Id, n.Kind, n.Channel, n.Status, n.Recipient, n.RedirectedTo != null, n.Attempts,
                n.CreatedAt, n.LastAttemptAt, n.SentAt, n.Error))
            .ToListAsync(ct);
    }
}
