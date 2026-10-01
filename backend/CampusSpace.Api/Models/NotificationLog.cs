namespace CampusSpace.Api.Models;

/// <summary>
/// One email to a requester about one status change (plan §8 NotificationLogs, §14). It is an outbox row: AppDbContext
/// adds it Pending in the same SaveChanges as the RequestStatusHistory row that caused it, and NotificationDispatcher sends
/// it later and records the outcome. It stores who it went to and how it went, never the email body. Not IAuditable: it is
/// a log itself.
/// </summary>
public class NotificationLog : ITimestamped
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public BookingRequest Request { get; set; } = null!;
    /// <summary>The status change this email is about. Unique: one email per change.</summary>
    public long StatusHistoryId { get; set; }
    public RequestStatusHistory StatusHistory { get; set; } = null!;
    /// <summary>One of <see cref="NotificationKinds.All"/>.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>One of <see cref="NotificationChannels.All"/>.</summary>
    public string Channel { get; set; } = NotificationChannels.Email;
    /// <summary>The requester's address when the row was written (also when Email:RedirectAllTo sent it elsewhere).</summary>
    public string Recipient { get; set; } = string.Empty;
    /// <summary>Where it actually went when Email:RedirectAllTo was set; null otherwise.</summary>
    public string? RedirectedTo { get; set; }
    /// <summary>One of <see cref="NotificationStatuses.All"/>.</summary>
    public string Status { get; set; } = NotificationStatuses.Pending;
    public int Attempts { get; set; }
    /// <summary>A Pending row is due when this is null or past (a retry after a 429 or a connection failure).</summary>
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    /// <summary>A fixed, key-free description (never a provider body or exception message). Required when Failed.</summary>
    public string? Error { get; set; }
    /// <summary>The provider's message id, for tracing a delivery.</summary>
    public string? ProviderMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
