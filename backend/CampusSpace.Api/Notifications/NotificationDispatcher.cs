using CampusSpace.Api.Background;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Notifications;

/// <summary>
/// Sends the outbox (plan §14, §7.1 rule 6). Every Email:PollSeconds it (1) fails rows stuck in Sending, (2) claims due
/// Pending rows with FOR UPDATE SKIP LOCKED, marks them Sending and commits, then (3) sends each one outside any
/// transaction, in its own scope. A row is marked Sending before the call, so a crash mid-send ends Failed instead of
/// being sent twice (at most once). An email therefore never blocks, delays or rolls back a decision. With nothing due it
/// goes idle (no database query) until a new row is signalled (<see cref="WorkSignals.Email"/>), the next known due time
/// or the next Email:IdleSweepMinutes sweep, so Neon can scale to zero (<see cref="PollingSchedule"/>). Off when
/// Email:DispatcherEnabled is false (Testing); tests call <see cref="ProcessOnceAsync"/>.
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopes, IOptions<EmailOptions> options, WorkSignals signals, TimeProvider clock,
    IHostEnvironment environment,
    ILogger<NotificationDispatcher> logger)
    : BackgroundService
{
    public const int BatchSize = 10;
    public static readonly TimeSpan StaleSendingAfter = TimeSpan.FromMinutes(2);
    public const string InterruptedMessage = "Interrupted while sending; not retried (it may have been sent)";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        // The mode only, never a value.
        logger.LogInformation("Email: {Mode}, redirect {Redirect}", o.IsConfigured ? "Brevo" : "disabled (no Email:BrevoApiKey; emails are Skipped)",
            o.Redirect is null ? "off" : "on");
        // The seeded demo addresses (@campusspace.local) are fake: in Production every email must go to one real inbox.
        if (environment.IsProduction() && o.IsConfigured && o.Redirect is null)
            logger.LogWarning("Email: Production sends to the seeded demo addresses, which are fake; set Email__RedirectAllTo");
        if (!o.DispatcherEnabled)
        {
            logger.LogInformation("Notification dispatcher is disabled (Email:DispatcherEnabled = false)");
            return;
        }

        logger.LogInformation("Notification dispatcher: every {PollSeconds} s while emails are due, else idle with a sweep every {SweepMinutes} min",
            o.PollSeconds, o.IdleSweepMinutes);
        try
        {
            await SignalledPollingLoop.RunAsync(TickAsync, signals.Email, TimeSpan.FromSeconds(o.PollSeconds),
                TimeSpan.FromMinutes(o.IdleSweepMinutes), clock, logger, "Notification dispatch", stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task<TickResult> TickAsync(CancellationToken ct) =>
        await ProcessOnceAsync(ct) > 0 ? new TickResult(FoundWork: true) : new TickResult(false, await NextDueAsync(ct));

    /// <summary>
    /// The earliest moment a row needs the dispatcher: a Pending row's NextAttemptAt (now when it has none), or when a
    /// Sending row becomes stale. Null when there is neither, so the loop can idle until a signal or its sweep.
    /// </summary>
    public async Task<DateTimeOffset?> NextDueAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.NotificationLogs.AsNoTracking()
            .Where(n => n.Status == NotificationStatuses.Pending || n.Status == NotificationStatuses.Sending)
            .Select(n => n.Status == NotificationStatuses.Pending
                ? n.NextAttemptAt ?? now
                : (n.LastAttemptAt ?? now) + StaleSendingAfter)
            .OrderBy(d => d)
            .Select(d => (DateTime?)d)
            .FirstOrDefaultAsync(ct);
        return due is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null;
    }

    /// <summary>One tick. Returns how many rows it tried to send.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct = default)
    {
        List<long> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = clock.GetUtcNow().UtcDateTime;

            var stale = now - StaleSendingAfter;
            var interrupted = await db.NotificationLogs
                .Where(n => n.Status == NotificationStatuses.Sending && n.LastAttemptAt < stale)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Status, NotificationStatuses.Failed)
                    .SetProperty(n => n.Error, InterruptedMessage)
                    .SetProperty(n => n.UpdatedAt, now), ct);
            if (interrupted > 0)
                logger.LogWarning("{Count} notification(s) were interrupted while sending and are now Failed", interrupted);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var due = await db.NotificationLogs
                .FromSql($"""
                    SELECT * FROM "NotificationLogs"
                    WHERE "Status" = {NotificationStatuses.Pending} AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {now})
                    ORDER BY "Id" LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);
            foreach (var row in due)
            {
                row.Status = NotificationStatuses.Sending;
                row.Attempts++;
                row.LastAttemptAt = now;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            ids = due.Select(n => n.Id).ToList();
        }

        foreach (var id in ids)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<INotificationDelivery>().DeliverAsync(id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The row stays Sending and the stale sweep fails it: never sent twice.
                logger.LogError(ex, "Notification {NotificationId} could not be recorded; continuing with the next one", id);
            }
        }
        return ids.Count;
    }
}
