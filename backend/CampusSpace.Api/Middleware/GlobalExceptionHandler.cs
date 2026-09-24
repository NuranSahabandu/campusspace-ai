using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CampusSpace.Api.Middleware;

/// <summary>
/// Last-resort handler for unhandled exceptions. Logs everything server-side and returns
/// RFC 9457 Problem Details without exception details. The traceId links the two.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public const string UniqueViolation = "23505";
    public const string ExclusionViolation = "23P01";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        var (status, title) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception. TraceId {TraceId}", traceId);
        else
            logger.LogWarning(exception, "Request failed with {Status} {Title}. TraceId {TraceId}", status, title, traceId);

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title },
        });
    }

    public static (int Status, string Title) Map(Exception exception)
    {
        // EF Core wraps database errors in DbUpdateException; look for the PostgresException inside.
        var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;
        return postgres?.SqlState switch
        {
            UniqueViolation => (StatusCodes.Status409Conflict, "Duplicate value"),
            ExclusionViolation => (StatusCodes.Status409Conflict, "Time slot was just booked"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };
    }
}
