namespace CampusSpace.Api.Dtos.Common;

/// <summary>Shared list response (§9): { items, page, pageSize, total }. Total counts every match, not just this page.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
