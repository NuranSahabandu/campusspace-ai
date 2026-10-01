using System.Linq.Expressions;
using CampusSpace.Api.Dtos.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

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
    /// The only case-insensitive "contains" search: keeps rows where ANY of the columns contains the trimmed search,
    /// with %, _ and \ matched literally. A null or blank search returns the query unchanged. A null column (an
    /// optional navigation such as r.Club!.Name) never matches. Never call EF.Functions.ILike/Like anywhere else:
    /// the two-argument ILike is translated with ESCAPE '' (no escape at all), so the escaped pattern would be
    /// matched literally (backslashes included) and the search would find nothing. SearchHelperGuardTests enforces it.
    /// </summary>
    public static IQueryable<T> WhereContains<T>(
        this IQueryable<T> query, string? search, params Expression<Func<T, string?>>[] columns)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;
        if (columns.Length == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));

        // Captured, so EF sends the pattern as a SQL parameter.
        var pattern = ToContainsPattern(search);
        Expression<Func<string?, bool>> matches = v => EF.Functions.ILike(v!, pattern, LikeEscape);

        var row = Expression.Parameter(typeof(T), "x");
        Expression? body = null;
        foreach (var column in columns)
        {
            var value = ReplacingExpressionVisitor.Replace(column.Parameters[0], row, column.Body);
            var match = ReplacingExpressionVisitor.Replace(matches.Parameters[0], value, matches.Body);
            body = body is null ? match : Expression.OrElse(body, match);
        }
        return query.Where(Expression.Lambda<Func<T, bool>>(body!, row));
    }

    /// <summary>The escape character <see cref="ToContainsPattern"/> uses, passed explicitly to ILike.</summary>
    private const string LikeEscape = "\\";

    /// <summary>Escapes LIKE wildcards with <see cref="LikeEscape"/> so user input matches literally.</summary>
    private static string ToContainsPattern(string search) =>
        "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
