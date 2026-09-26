using CampusSpace.Api.Dtos.AuditLogs;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Services;

/// <summary>
/// Audit events that are not entity changes (entity changes are audited by AppDbContext automatically).
/// Details must never contain passwords, tokens or other secrets. Both Log methods call SaveChangesAsync,
/// so they also save anything else pending on the request's AppDbContext.
/// </summary>
public interface IAuditService
{
    /// <summary>Logs an event by the current caller (ICurrentUser; null when anonymous).</summary>
    Task LogAsync(string action, string entityType, string? entityId, object? details = null, CancellationToken ct = default);

    /// <summary>Logs an event attributed to <paramref name="userId"/> instead of the caller, for example a login.</summary>
    Task LogForUserAsync(long? userId, string action, string entityType, string? entityId, object? details = null, CancellationToken ct = default);

    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogsQuery query, CancellationToken ct = default);
}
