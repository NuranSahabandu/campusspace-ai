using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Common;

/// <summary>
/// Shared list query (§9): ?search=&amp;sort=&amp;page=&amp;pageSize=. Bind with [FromQuery].
/// Sort is a field name, prefixed with "-" for descending; each list decides which fields it allows.
/// </summary>
public record PageQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    [MaxLength(100)] public string? Search { get; init; }
    [MaxLength(50)] public string? Sort { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, MaxPageSize)] public int PageSize { get; init; } = DefaultPageSize;
}
