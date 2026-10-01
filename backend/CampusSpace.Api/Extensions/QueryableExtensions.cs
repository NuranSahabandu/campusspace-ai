using CampusSpace.Api.Dtos.Common;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Extensions;

public static class QueryableExtensions
{
    /// <summary>Counts the filtered query, then fetches one page. Apply filters and a stable ORDER BY first.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// The escape character <see cref="ToContainsPattern"/> uses. Pass it to EF.Functions.ILike(value, pattern, LikeEscape):
    /// the two-argument ILike is translated with ESCAPE '' (no escape at all), so the backslashes would be matched literally.
    /// </summary>
    public const string LikeEscape = "\\";

    /// <summary>Escapes LIKE wildcards so user input matches literally (PostgreSQL's default escape is '\').</summary>
    public static string ToContainsPattern(this string search) =>
        "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
