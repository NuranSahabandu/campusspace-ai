using System.Diagnostics;
using CampusSpace.Api.Data.Configurations;
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
    public const string ForeignKeyViolation = "23503";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        var (status, title) = Map(exception);
        var problem = exception is BusinessRuleException rule
            // Same shape as a data-annotation failure, so clients reuse their field-error handling.
            ? new HttpValidationProblemDetails(rule.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = title;

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception. TraceId {TraceId}", traceId);
        else
            logger.LogWarning(exception, "Request failed with {Status} {Title}. TraceId {TraceId}", status, title, traceId);

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    public static (int Status, string Title) Map(Exception exception)
    {
        if (exception is ConflictException conflict)
            return (StatusCodes.Status409Conflict, conflict.Message);
        if (exception is BusinessRuleException rule)
            return (StatusCodes.Status400BadRequest, rule.Message);
        if (exception is ForbiddenException forbidden)
            return (StatusCodes.Status403Forbidden, forbidden.Message);

        // EF Core wraps database errors in DbUpdateException; look for the PostgresException inside.
        var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;
        return postgres?.SqlState switch
        {
            UniqueViolation when postgres.ConstraintName == ClubMemberConfiguration.OneRepresentativeIndex =>
                (StatusCodes.Status409Conflict, "Club already has a representative"),
            UniqueViolation when postgres.ConstraintName == PricingRuleConfiguration.UniqueRuleIndex =>
                (StatusCodes.Status409Conflict, PricingRuleConfiguration.DuplicateRuleMessage),
            UniqueViolation when postgres.ConstraintName == QuotationConfiguration.LiveQuoteIndex =>
                (StatusCodes.Status409Conflict, QuotationConfiguration.LiveQuoteMessage),
            UniqueViolation when postgres.ConstraintName == AgentRunConfiguration.LiveRunIndex =>
                (StatusCodes.Status409Conflict, AgentRunConfiguration.LiveRunMessage),
            UniqueViolation when postgres.ConstraintName == EquipmentLoanConfiguration.OneOpenLoanIndex =>
                (StatusCodes.Status409Conflict, EquipmentLoanConfiguration.ItemOnLoanMessage),
            UniqueViolation => (StatusCodes.Status409Conflict, "Duplicate value"),
            // Double booking (BookingConfiguration.NoRoomOverlapConstraint, the only exclusion constraint): the approval
            // that commits second loses (§8.2).
            ExclusionViolation => (StatusCodes.Status409Conflict, "Time slot was just booked"),
            // Services check FKs before inserting (400 on the field), so this is a delete of a row that is still referenced.
            ForeignKeyViolation => (StatusCodes.Status409Conflict, "In use"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };
    }
}
