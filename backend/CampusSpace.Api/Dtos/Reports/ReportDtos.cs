using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Reports;

/// <summary>
/// GET /api/reports/utilization and /api/reports/demand. From/To are campus dates (yyyy-MM-dd), inclusive, both required,
/// From ≤ To and at most <see cref="MaxDays"/> days.
/// </summary>
public record ReportRangeQuery : IValidatableObject
{
    public const int MaxDays = 366;

    [Required] public DateOnly? From { get; init; }
    [Required] public DateOnly? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is not { } from || To is not { } to)
            yield break;
        if (from > to)
            yield return new ValidationResult("From must not be later than To.", [nameof(From)]);
        else if (to.DayNumber - from.DayNumber + 1 > MaxDays)
            yield return new ValidationResult($"The range can be at most {MaxDays} days.", [nameof(To)]);
    }
}

/// <summary>
/// Booked and available hours (2 dp) and Utilization = booked ÷ available seconds (a fraction, 4 dp), null when nothing
/// was available.
/// </summary>
public record UtilizationFigures(decimal BookedHours, decimal AvailableHours, double? Utilization);

/// <summary>One building: the ratio of its active rooms' sums, never an average of their percentages.</summary>
public record BuildingUtilizationDto(long BuildingId, string Code, string Name, int Rooms, UtilizationFigures Figures);

/// <summary>
/// One room. IsActive false: the room is inactive now, so it is left out of the building and overall totals; it is listed
/// only when it has booked hours in the range.
/// </summary>
public record RoomUtilizationDto(
    long RoomId, string Code, string Name, long BuildingId, string BuildingCode, bool IsActive, UtilizationFigures Figures);

/// <summary>
/// Utilization in [From, To] (UC22). Available = the CURRENT policy's opening hours of every campus day minus blackouts;
/// booked = Confirmed, CheckedIn and Completed bookings clipped to those hours, minus blackouts. Opening hours and the
/// active flag are today's values (their history isn't stored), so a past range is measured against them.
/// </summary>
public record UtilizationReportDto(
    DateOnly From, DateOnly To, UtilizationFigures Overall,
    IReadOnlyList<BuildingUtilizationDto> Buildings, IReadOnlyList<RoomUtilizationDto> Rooms);

public record DayCountDto(DateOnly Date, int Count);

/// <summary>Hour is the campus clock hour (0–23).</summary>
public record HourCountDto(int Hour, int Count);

/// <summary>
/// Outcomes whose status change happened in the range (one per request each). ApprovalRate = Approved ÷ Decided,
/// Decided = Approved + OfficerRejected (the officer's verdicts on a proposal). Not in the rate: ClosedAutomatically
/// (system Rejected, the time became invalid), CancelledBeforeDecision (Cancelled from a non-Approved status),
/// AgentFailed (no proposal reached the officer) and RevisionsRequested (officer revise; the request goes on).
/// </summary>
public record ApprovalOutcomesDto(
    int Approved, int OfficerRejected, int Decided, double? ApprovalRate,
    int ClosedAutomatically, int CancelledBeforeDecision, int AgentFailed, int RevisionsRequested);

/// <summary>
/// Demand in [From, To] (UC22). Total, ByDay and ByHour count the requests submitted (CreatedAt) in the range: ByDay by
/// campus date of submission, ByHour by campus hour of the requested start; every day and all 24 hours are listed.
/// </summary>
public record DemandReportDto(
    DateOnly From, DateOnly To, int Total,
    IReadOnlyList<DayCountDto> ByDay, IReadOnlyList<HourCountDto> ByHour, ApprovalOutcomesDto Approvals);

/// <summary>
/// The agent KPIs of the dashboard, taken unchanged from GET /api/agent-runs/metrics for the same range: SuccessRate =
/// ReachedGate ÷ Finished; AvgProcessingMs = the reached-gate processing average over ProcessingRuns runs.
/// </summary>
public record DashboardAgentDto(double? SuccessRate, int ReachedGate, int Finished, int? AvgProcessingMs, int ProcessingRuns);

/// <summary>
/// GET /api/reports/dashboard. PendingApprovals is now; TodayBookings overlap campus today; everything else covers the
/// last 7 campus days [From, To = Today]. BookingsPerDay counts Confirmed, CheckedIn and Completed bookings by the campus
/// date of their start.
/// </summary>
public record DashboardDto(
    DateOnly Today, DateOnly From, DateOnly To,
    int PendingApprovals, int TodayBookings,
    UtilizationFigures Utilization, IReadOnlyList<BuildingUtilizationDto> UtilizationByBuilding,
    IReadOnlyList<DayCountDto> BookingsPerDay, DashboardAgentDto Agent);
