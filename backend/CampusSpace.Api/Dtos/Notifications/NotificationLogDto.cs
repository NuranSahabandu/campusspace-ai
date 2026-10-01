namespace CampusSpace.Api.Dtos.Notifications;

/// <summary>
/// One email about a request, for the Facilities Officer. Redirected says Email:RedirectAllTo received it instead (the
/// redirect address itself isn't shown). Error is a fixed, key-free text: render it as plain text.
/// </summary>
public sealed record NotificationLogDto(
    long Id, string Kind, string Channel, string Status, string Recipient, bool Redirected, int Attempts,
    DateTime CreatedAt, DateTime? LastAttemptAt, DateTime? SentAt, string? Error);
