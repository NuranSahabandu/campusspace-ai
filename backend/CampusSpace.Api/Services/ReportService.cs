using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.AgentRuns;
using CampusSpace.Api.Dtos.Reports;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CampusSpace.Api.Services;

public sealed class ReportService(
    AppDbContext db, IPolicySettingsService policy, IAgentRunMonitorService monitor, TimeProvider clock) : IReportService
{
    /// <summary>Bookings that count as booked time and as bookings per day: every status except Cancelled.</summary>
    public static readonly IReadOnlyList<string> CountedBookingStatuses =
        [BookingStatuses.Confirmed, BookingStatuses.CheckedIn, BookingStatuses.Completed];

    /// <summary>The dashboard covers the last 7 campus days, today included (the 5.1 monitor's default range).</summary>
    public const int DashboardDays = 7;

    public async Task<UtilizationReportDto> GetUtilizationAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (overall, buildings, rooms) = await UtilizationAsync(from, to, ct);
        return new UtilizationReportDto(from, to, overall, buildings, rooms);
    }

    public async Task<DemandReportDto> GetDemandAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var byDay = await db.Database.SqlQueryRaw<DayCountRow>(DemandByDaySql, RangeParameters(from, to)).ToListAsync(ct);
        var byHour = await db.Database.SqlQueryRaw<HourCountRow>(DemandByHourSql, RangeParameters(from, to)).ToListAsync(ct);
        var o = (await db.Database.SqlQueryRaw<OutcomeRow>(OutcomesSql, RangeParameters(from, to)).ToListAsync(ct)).Single();

        var decided = o.Approved + o.OfficerRejected;
        var approvals = new ApprovalOutcomesDto(
            o.Approved, o.OfficerRejected, decided, Rate(o.Approved, decided),
            o.ClosedAutomatically, o.CancelledBeforeDecision, o.AgentFailed, o.RevisionsRequested);

        return new DemandReportDto(
            from, to, byDay.Sum(d => d.Count),
            byDay.Select(d => new DayCountDto(d.Day, d.Count)).ToList(),
            byHour.Select(h => new HourCountDto(h.Hour, h.Count)).ToList(),
            approvals);
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var today = CampusTime.Today(clock);
        var from = today.AddDays(-(DashboardDays - 1));

        var pending = await db.BookingRequests.AsNoTracking().CountAsync(r => r.Status == RequestStatuses.PendingApproval, ct);
        var todayRange = CampusTime.UtcRange(CampusTime.StartOf(today), CampusTime.StartOf(today.AddDays(1)));
        var todayBookings = await db.Bookings.AsNoTracking()
            .CountAsync(b => CountedBookingStatuses.Contains(b.Status) && b.TimeRange.Overlaps(todayRange), ct);

        var (overall, buildings, _) = await UtilizationAsync(from, today, ct);
        var perDay = await db.Database.SqlQueryRaw<DayCountRow>(BookingsPerDaySql, RangeParameters(from, today)).ToListAsync(ct);

        // The 5.1 definitions, unchanged: success = reached the gate ÷ finished; processing = Σ step time of runs that reached it.
        var runs = (await monitor.GetMetricsAsync(new AgentRunMetricsQuery { From = from, To = today }, ct)).Runs;
        var agent = new DashboardAgentDto(
            runs.SuccessRate, runs.ReachedGate, runs.Finished, runs.ReachedGateProcessing.AvgMs, runs.ReachedGateProcessing.Runs);

        return new DashboardDto(
            today, from, today, pending, todayBookings, overall, buildings,
            perDay.Select(d => new DayCountDto(d.Day, d.Count)).ToList(), agent);
    }

    // ---------- utilization ----------

    private async Task<(UtilizationFigures Overall, List<BuildingUtilizationDto> Buildings, List<RoomUtilizationDto> Rooms)>
        UtilizationAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Opening hours come from the CURRENT policy (its history isn't stored) and reach SQL only as parameters.
        var snapshot = await policy.GetAsync(ct);
        var open = PolicySnapshot.Days
            .Select(d => (Iso: IsoDay(d.Day), Hours: snapshot.OpeningHours[d.Day]))
            .Where(d => d.Hours is not null)
            .ToList();

        var parameters = RangeParameters(from, to).Concat(new NpgsqlParameter[]
        {
            new("dows", open.Select(d => d.Iso).ToArray()),
            new("opens", open.Select(d => d.Hours!.Open).ToArray()) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Time },
            new("closes", open.Select(d => d.Hours!.Close).ToArray()) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Time },
        }).ToArray();
        var rows = await db.Database.SqlQueryRaw<RoomRow>(UtilizationSql, parameters).ToListAsync(ct);

        var active = rows.Where(r => r.IsActive).ToList();
        var buildings = active
            .GroupBy(r => (r.BuildingId, r.BuildingCode, r.BuildingName))
            .OrderBy(g => g.Key.BuildingCode, StringComparer.Ordinal)
            .Select(g => new BuildingUtilizationDto(
                g.Key.BuildingId, g.Key.BuildingCode, g.Key.BuildingName, g.Count(),
                Figures(g.Sum(r => r.BookedSeconds), g.Sum(r => r.AvailableSeconds))))
            .ToList();
        var rooms = rows
            .Where(r => r.IsActive || r.BookedSeconds > 0)
            .Select(r => new RoomUtilizationDto(
                r.RoomId, r.Code, r.Name, r.BuildingId, r.BuildingCode, r.IsActive, Figures(r.BookedSeconds, r.AvailableSeconds)))
            .ToList();
        var overall = Figures(active.Sum(r => r.BookedSeconds), active.Sum(r => r.AvailableSeconds));
        return (overall, buildings, rooms);
    }

    /// <summary>Totals are sums of seconds, so a building or the campus is Σ booked ÷ Σ available, not a mean of rates.</summary>
    private static UtilizationFigures Figures(decimal bookedSeconds, decimal availableSeconds) => new(
        Hours(bookedSeconds), Hours(availableSeconds),
        availableSeconds == 0 ? null : Math.Round((double)(bookedSeconds / availableSeconds), 4));

    private static decimal Hours(decimal seconds) => Math.Round(seconds / 3600m, 2, MidpointRounding.AwayFromZero);

    /// <summary>ISO weekday, as Postgres extract(isodow): Monday 1 … Sunday 7.</summary>
    private static int IsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    // ---------- SQL ----------
    // Only code constants are spliced into these strings; dates, opening hours and the campus offset are parameters.

    /// <summary>
    /// @from/@to are campus dates; @fromUtc/@toUtc are campus midnight of From and of the day after To; @offset is
    /// CampusTime.Offset. A campus date or hour of an instant is (instant AT TIME ZONE 'UTC') + @offset.
    /// </summary>
    private static NpgsqlParameter[] RangeParameters(DateOnly from, DateOnly to) =>
    [
        new("from", from), new("to", to),
        new("fromUtc", CampusTime.StartOf(from).UtcDateTime), new("toUtc", CampusTime.StartOf(to.AddDays(1)).UtcDateTime),
        new("offset", CampusTime.Offset),
    ];

    private static string CampusDate(string instant) => $"""(({instant} AT TIME ZONE 'UTC') + @offset)::date""";

    /// <summary>Campus days as timestamp (without time zone), so nothing depends on the session TimeZone.</summary>
    private const string Days = "generate_series(@from::timestamp, @to::timestamp, interval '1 day')";

    private static string SqlList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

    /// <summary>Total seconds of a tstzmultirange.</summary>
    private static string Seconds(string multirange) =>
        $"""(SELECT COALESCE(sum(extract(epoch FROM upper(x) - lower(x))), 0) FROM unnest({multirange}) AS x)""";

    /// <summary>
    /// One row per room. A window is one campus day's opening hours as a UTC tstzrange [open, close); closed days have
    /// none. blocked/booked are range_agg unions clipped to the window, so overlapping blackouts (or a Completed booking,
    /// which no_room_overlap doesn't cover) count once. available = window − blocked; booked = booked − blocked, so time
    /// booked inside a blackout counts in neither and booked ≤ available.
    /// </summary>
    private static readonly string UtilizationSql = $"""
        WITH windows AS (
          SELECT tstzrange(((d::date + h.open) - @offset) AT TIME ZONE 'UTC',
                           ((d::date + h.close) - @offset) AT TIME ZONE 'UTC', '[)') AS w
          FROM {Days} AS d
          JOIN unnest(@dows, @opens, @closes) AS h(dow, open, close) ON extract(isodow FROM d)::int = h.dow
        ),
        room_windows AS (
          SELECT r."Id" AS room_id, w.w::tstzmultirange AS w,
                 COALESCE((SELECT range_agg(b."TimeRange" * w.w) FROM "RoomBlackouts" b
                           WHERE b."RoomId" = r."Id" AND b."TimeRange" && w.w), tstzmultirange()) AS blocked,
                 COALESCE((SELECT range_agg(k."TimeRange" * w.w) FROM "Bookings" k
                           WHERE k."RoomId" = r."Id" AND k."Status" IN ({SqlList(CountedBookingStatuses)})
                             AND k."TimeRange" && w.w), tstzmultirange()) AS booked
          FROM "Rooms" r
          CROSS JOIN windows w
        ),
        per_room AS (
          SELECT room_id, sum({Seconds("w - blocked")}) AS available, sum({Seconds("booked - blocked")}) AS booked
          FROM room_windows
          GROUP BY room_id
        )
        SELECT r."Id" AS "RoomId", r."Code", r."Name", r."IsActive",
               b."Id" AS "BuildingId", b."Code" AS "BuildingCode", b."Name" AS "BuildingName",
               COALESCE(p.available, 0)::numeric AS "AvailableSeconds", COALESCE(p.booked, 0)::numeric AS "BookedSeconds"
        FROM "Rooms" r
        JOIN "Buildings" b ON b."Id" = r."BuildingId"
        LEFT JOIN per_room p ON p.room_id = r."Id"
        ORDER BY b."Code", r."Code"
        """;

    /// <summary>Requests submitted per campus day (CreatedAt is the submission; there are no drafts), zero-filled.</summary>
    private static readonly string DemandByDaySql = $"""
        SELECT d::date AS "Day", count(r."Id")::int AS "Count"
        FROM {Days} AS d
        LEFT JOIN "BookingRequests" r
          ON r."CreatedAt" >= @fromUtc AND r."CreatedAt" < @toUtc AND {CampusDate("r.\"CreatedAt\"")} = d::date
        GROUP BY d
        ORDER BY d
        """;

    /// <summary>The same requests by campus hour of the requested start, all 24 hours.</summary>
    private static readonly string DemandByHourSql = """
        SELECT h AS "Hour", count(r."Id")::int AS "Count"
        FROM generate_series(0, 23) AS h
        LEFT JOIN "BookingRequests" r
          ON r."CreatedAt" >= @fromUtc AND r."CreatedAt" < @toUtc
         AND extract(hour FROM (r."RequestedStart" AT TIME ZONE 'UTC') + @offset)::int = h
        GROUP BY h
        ORDER BY h
        """;

    /// <summary>
    /// Outcomes by the date of the status change (one per request each). Approved is always an officer's approval;
    /// Rejected with an actor is the officer's, without one the system closed it (the time became invalid).
    /// </summary>
    private static readonly string OutcomesSql = $"""
        WITH h AS (
          SELECT * FROM "RequestStatusHistory" WHERE "ChangedAt" >= @fromUtc AND "ChangedAt" < @toUtc
        )
        SELECT count(DISTINCT "RequestId") FILTER (WHERE "ToStatus" = '{RequestStatuses.Approved}')::int AS "Approved",
               count(DISTINCT "RequestId") FILTER (WHERE "ToStatus" = '{RequestStatuses.Rejected}' AND "ChangedById" IS NOT NULL)::int AS "OfficerRejected",
               count(DISTINCT "RequestId") FILTER (WHERE "ToStatus" = '{RequestStatuses.Rejected}' AND "ChangedById" IS NULL)::int AS "ClosedAutomatically",
               count(DISTINCT "RequestId") FILTER (WHERE "ToStatus" = '{RequestStatuses.Cancelled}'
                                                     AND "FromStatus" IS DISTINCT FROM '{RequestStatuses.Approved}')::int AS "CancelledBeforeDecision",
               count(DISTINCT "RequestId") FILTER (WHERE "ToStatus" = '{RequestStatuses.AgentFailed}')::int AS "AgentFailed",
               count(DISTINCT "RequestId") FILTER (WHERE "ToStatus" = '{RequestStatuses.RevisionRequested}' AND "ChangedById" IS NOT NULL)::int AS "RevisionsRequested"
        FROM h
        """;

    /// <summary>Counted bookings by the campus date of their start, zero-filled.</summary>
    private static readonly string BookingsPerDaySql = $"""
        SELECT d::date AS "Day", count(k."Id")::int AS "Count"
        FROM {Days} AS d
        LEFT JOIN "Bookings" k
          ON k."Status" IN ({SqlList(CountedBookingStatuses)})
         AND lower(k."TimeRange") >= @fromUtc AND lower(k."TimeRange") < @toUtc
         AND {CampusDate("lower(k.\"TimeRange\")")} = d::date
        GROUP BY d
        ORDER BY d
        """;

    private static double? Rate(int numerator, int denominator) =>
        denominator == 0 ? null : Math.Round((double)numerator / denominator, 4);

    private sealed class RoomRow
    {
        public long RoomId { get; init; }
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public bool IsActive { get; init; }
        public long BuildingId { get; init; }
        public string BuildingCode { get; init; } = "";
        public string BuildingName { get; init; } = "";
        public decimal AvailableSeconds { get; init; }
        public decimal BookedSeconds { get; init; }
    }

    private sealed class DayCountRow
    {
        public DateOnly Day { get; init; }
        public int Count { get; init; }
    }

    private sealed class HourCountRow
    {
        public int Hour { get; init; }
        public int Count { get; init; }
    }

    private sealed class OutcomeRow
    {
        public int Approved { get; init; }
        public int OfficerRejected { get; init; }
        public int ClosedAutomatically { get; init; }
        public int CancelledBeforeDecision { get; init; }
        public int AgentFailed { get; init; }
        public int RevisionsRequested { get; init; }
    }
}
