namespace CampusSpace.Api.Notifications;

/// <summary>A file attached to an email (the approval's .ics).</summary>
public sealed record EmailAttachment(string Name, byte[] Content);

/// <summary>A rendered email. The dispatcher has already applied Email:RedirectAllTo to <see cref="To"/>.</summary>
public sealed record EmailMessage(string To, string ToName, string Subject, string Html, string Text, EmailAttachment? Attachment = null);

public enum EmailSendOutcome
{
    Sent,
    /// <summary>The provider can't have accepted it (a 429, or no connection was made): safe to send again later.</summary>
    Retry,
    /// <summary>Final: rejected, or it may already have been accepted (a timeout, a 5xx), so it is never sent again.</summary>
    Failed,
    /// <summary>Not sent on purpose (email isn't configured).</summary>
    Skipped,
}

/// <summary>
/// What happened to one send. <see cref="Error"/> is always one of the sender's fixed, key-free texts, never a provider
/// body or an exception message, because it is stored in NotificationLogs and shown to officers.
/// </summary>
public sealed record EmailSendResult(EmailSendOutcome Outcome, string? Error = null, string? MessageId = null, TimeSpan? RetryAfter = null)
{
    public static EmailSendResult Sent(string? messageId) => new(EmailSendOutcome.Sent, MessageId: messageId);
    public static EmailSendResult Retry(string error, TimeSpan? after = null) => new(EmailSendOutcome.Retry, error, RetryAfter: after);
    public static EmailSendResult Failed(string error) => new(EmailSendOutcome.Failed, error);
    public static EmailSendResult Skipped(string reason) => new(EmailSendOutcome.Skipped, reason);
}

/// <summary>Sends one email. Never throws for a provider answer or an outage, only for the caller's own cancellation.</summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Used when Email:BrevoApiKey is empty (CI, tests, a fresh clone): every email is recorded as Skipped.</summary>
public sealed class NoOpEmailSender : IEmailSender
{
    public const string NotConfiguredMessage = "Email is not configured (Email:BrevoApiKey is empty)";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default) =>
        Task.FromResult(EmailSendResult.Skipped(NotConfiguredMessage));
}
