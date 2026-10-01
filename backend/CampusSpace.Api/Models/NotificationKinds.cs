namespace CampusSpace.Api.Models;

/// <summary>The only list of notification kinds: which status change an email is about (see NotificationOutbox).</summary>
public static class NotificationKinds
{
    public const string Approved = "Approved";
    /// <summary>Rejected by a Facilities Officer, with their reason.</summary>
    public const string Rejected = "Rejected";
    /// <summary>Rejected by the system (the approval's time close): neutral text, no reason.</summary>
    public const string Closed = "Closed";
    /// <summary>A new proposal is being prepared (an officer's revise or a failed approval): no notes, no reason.</summary>
    public const string RevisionRequested = "RevisionRequested";
    /// <summary>Cancelled by a Facilities Officer, with their reason.</summary>
    public const string CancelledByOfficer = "CancelledByOfficer";

    public static readonly IReadOnlyList<string> All = [Approved, Rejected, Closed, RevisionRequested, CancelledByOfficer];
}
