namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of notification statuses. Pending → Sending → Sent | Failed | Skipped, or back to Pending for a retry
/// that can't duplicate a send. Sent, Failed and Skipped are final.
/// </summary>
public static class NotificationStatuses
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    /// <summary>Not sent on purpose: email isn't configured, or the email no longer applies.</summary>
    public const string Skipped = "Skipped";

    public static readonly IReadOnlyList<string> All = [Pending, Sending, Sent, Failed, Skipped];
}
