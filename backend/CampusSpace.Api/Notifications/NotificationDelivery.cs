using CampusSpace.Api.Agents;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Notifications;

/// <summary>Sends one claimed (Sending) NotificationLog and records the outcome. One scope per row.</summary>
public interface INotificationDelivery
{
    Task DeliverAsync(long notificationId, CancellationToken ct = default);
}

/// <summary>
/// Renders the email at send time from current data (no body is stored), applies Email:RedirectAllTo, sends it and
/// updates the row: Sent, Failed, Skipped, or Pending again for a retry that can't send twice (see BrevoEmailSender).
/// </summary>
public sealed class NotificationDelivery(
    AppDbContext db, IEmailSender sender, IOptions<EmailOptions> options, TimeProvider clock, ILogger<NotificationDelivery> logger)
    : INotificationDelivery
{
    public const string NoLongerApprovedMessage = "The request is no longer approved";

    /// <summary>Wait before the 2nd and 3rd attempt after a connection failure (a 429 uses its Retry-After instead).</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryBackoff = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

    public async Task DeliverAsync(long notificationId, CancellationToken ct = default)
    {
        var row = await db.NotificationLogs.SingleOrDefaultAsync(n => n.Id == notificationId, ct);
        if (row is not { Status: NotificationStatuses.Sending })
            return;

        EmailSendResult result;
        string? redirect = null;
        try
        {
            var (email, skipReason) = await ComposeAsync(row, ct);
            if (email is null)
            {
                result = EmailSendResult.Skipped(skipReason!);
            }
            else
            {
                redirect = options.Value.Redirect;
                result = await sender.SendAsync(redirect is null ? email : email with
                {
                    To = redirect,
                    Subject = EmailTemplates.RedirectedPrefix + email.Subject,
                }, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Notification {NotificationId} could not be sent", row.Id);
            result = EmailSendResult.Failed($"Unexpected error ({ex.GetType().Name})");
        }

        Record(row, result, redirect);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Notification {NotificationId} ({Kind}) attempt {Attempt}: {Status}",
            row.Id, row.Kind, row.Attempts, row.Status);
    }

    private void Record(NotificationLog row, EmailSendResult result, string? redirect)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (result.Outcome != EmailSendOutcome.Skipped)
            row.RedirectedTo = redirect;
        row.Error = result.Error is null ? null : AgentTrace.Truncate(result.Error, NotificationLogConfiguration.ErrorMaxLength);
        row.NextAttemptAt = null;
        switch (result.Outcome)
        {
            case EmailSendOutcome.Sent:
                row.Status = NotificationStatuses.Sent;
                row.SentAt = now;
                row.ProviderMessageId = result.MessageId;
                break;
            case EmailSendOutcome.Skipped:
                row.Status = NotificationStatuses.Skipped;
                break;
            case EmailSendOutcome.Retry when row.Attempts < NotificationLogConfiguration.MaxAttempts:
                row.Status = NotificationStatuses.Pending;
                row.NextAttemptAt = now + (result.RetryAfter ?? RetryBackoff[Math.Min(row.Attempts, RetryBackoff.Count) - 1]);
                break;
            case EmailSendOutcome.Retry:
                row.Status = NotificationStatuses.Failed;
                row.Error = AgentTrace.Truncate($"Gave up after {row.Attempts} attempts: {result.Error}",
                    NotificationLogConfiguration.ErrorMaxLength);
                break;
            default:
                row.Status = NotificationStatuses.Failed;
                row.Error ??= "Failed";
                break;
        }
    }

    /// <summary>The email for the row from the current data, or the reason it no longer applies.</summary>
    private async Task<(EmailMessage? Email, string? SkipReason)> ComposeAsync(NotificationLog row, CancellationToken ct)
    {
        var data = await db.NotificationLogs.AsNoTracking().Where(n => n.Id == row.Id)
            .Select(n => new
            {
                Name = n.Request.Requester.FullName,
                n.Request.Purpose,
                n.Request.RequestedStart,
                n.Request.RequestedEnd,
                RequestStatus = n.Request.Status,
                n.StatusHistory.Reason,
            })
            .SingleAsync(ct);

        var model = new EmailModel(row.Kind, data.Name, data.Purpose,
            AgentTrace.Utc(data.RequestedStart), AgentTrace.Utc(data.RequestedEnd));
        EmailAttachment? attachment = null;
        switch (row.Kind)
        {
            case NotificationKinds.Approved:
                if (data.RequestStatus != RequestStatuses.Approved)
                    return (null, NoLongerApprovedMessage);
                var booking = await db.Bookings.AsNoTracking()
                    .Where(b => b.RequestId == row.RequestId && BookingStatuses.Active.Contains(b.Status))
                    .Select(b => new { b.Id, b.TimeRange, Room = b.Room.Name, Building = b.Room.Building.Name })
                    .SingleOrDefaultAsync(ct);
                if (booking is null)
                    return (null, NoLongerApprovedMessage);
                var quote = await db.Quotations.AsNoTracking()
                    .Where(q => q.RequestId == row.RequestId && q.Status == QuotationStatuses.Issued)
                    .Select(q => new { q.Total, q.IsExempt })
                    .SingleOrDefaultAsync(ct);
                var location = $"{booking.Room}, {booking.Building}";
                var start = AgentTrace.Utc(booking.TimeRange.LowerBound);
                var end = AgentTrace.Utc(booking.TimeRange.UpperBound);
                model = model with { Start = start, End = end, Location = location, Total = quote?.Total, IsExempt = quote?.IsExempt ?? false };
                attachment = new EmailAttachment("booking.ics", System.Text.Encoding.UTF8.GetBytes(IcsCalendar.Build(
                    booking.Id, data.Purpose, location, start.UtcDateTime, end.UtcDateTime, clock.GetUtcNow().UtcDateTime)));
                break;
            case NotificationKinds.Rejected or NotificationKinds.CancelledByOfficer:
                model = model with { Reason = data.Reason };
                break;
            case NotificationKinds.Closed when !NotificationOutbox.IsTimeClose(data.Reason):
                logger.LogWarning("Notification {NotificationId}: a system rejection without the time-close marker; sent as Closed", row.Id);
                break;
        }

        var rendered = EmailTemplates.Render(model);
        return (new EmailMessage(row.Recipient, data.Name, rendered.Subject, rendered.Html, rendered.Text, attachment), null);
    }
}
