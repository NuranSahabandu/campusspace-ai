using System.Text.Json;

namespace CampusSpace.Api.Dtos.AuditLogs;

/// <summary>One audit row. UserName is null for anonymous events or a deleted user. Details is the stored JSON object.</summary>
public record AuditLogDto(
    long Id,
    long? UserId,
    string? UserName,
    string Action,
    string EntityType,
    string? EntityId,
    JsonElement Details,
    DateTime At);
