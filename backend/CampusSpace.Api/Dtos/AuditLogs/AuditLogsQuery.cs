using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.AuditLogs;

/// <summary>
/// GET /api/audit-logs. Every filter is optional and exact, except Search (entity id or user name, contains).
/// From/To are inclusive instants; send them with an offset (for example 2026-09-26T00:00:00Z).
/// Newest first by default; Sort accepts only "at" or "-at".
/// </summary>
public record AuditLogsQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["at"];

    [MaxLength(100)] public string? EntityType { get; init; }
    [MaxLength(50)] public string? Action { get; init; }
    public long? UserId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is not null && To is not null && From > To)
            yield return new ValidationResult("From must not be later than To.", [nameof(From)]);

        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}
