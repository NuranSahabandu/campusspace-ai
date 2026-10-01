using System.Text.Json;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.AuditLogs;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class AuditService(AppDbContext db, ICurrentUser currentUser) : IAuditService
{
    // camelCase, the same as API responses.
    private static readonly JsonSerializerOptions DetailsJsonOptions = new(JsonSerializerDefaults.Web);

    public Task LogAsync(string action, string entityType, string? entityId, object? details = null, CancellationToken ct = default)
        => LogForUserAsync(currentUser.UserId, action, entityType, entityId, details, ct);

    public async Task LogForUserAsync(
        long? userId, string action, string entityType, string? entityId, object? details = null, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = details is null ? "{}" : JsonSerializer.Serialize(details, DetailsJsonOptions),
            At = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogsQuery query, CancellationToken ct = default)
    {
        var logs = db.AuditLogs.AsNoTracking();

        if (query.EntityType is not null)
            logs = logs.Where(a => a.EntityType == query.EntityType);
        if (query.Action is not null)
            logs = logs.Where(a => a.Action == query.Action);
        if (query.UserId is not null)
            logs = logs.Where(a => a.UserId == query.UserId);
        // timestamptz parameters must be UTC.
        if (query.From is { } from)
            logs = logs.Where(a => a.At >= from.UtcDateTime);
        if (query.To is { } to)
            logs = logs.Where(a => a.At <= to.UtcDateTime);
        logs = logs.WhereContains(query.Search, a => a.EntityId, a => a.User!.FullName);

        // Id breaks ties between rows written in the same save (same At).
        logs = query.Sort == "at"
            ? logs.OrderBy(a => a.At).ThenBy(a => a.Id)
            : logs.OrderByDescending(a => a.At).ThenByDescending(a => a.Id);

        var page = await logs
            .Select(a => new Row(a.Id, a.UserId, a.User != null ? a.User.FullName : null,
                a.Action, a.EntityType, a.EntityId, a.DetailsJson, a.At))
            .ToPagedResultAsync(query, ct);

        var items = page.Items.Select(r => new AuditLogDto(r.Id, r.UserId, r.UserName, r.Action, r.EntityType, r.EntityId,
            JsonDocument.Parse(r.DetailsJson).RootElement.Clone(), r.At)).ToList();
        return new PagedResult<AuditLogDto>(items, page.Page, page.PageSize, page.Total);
    }

    private sealed record Row(
        long Id, long? UserId, string? UserName, string Action, string EntityType, string? EntityId, string DetailsJson, DateTime At);
}
