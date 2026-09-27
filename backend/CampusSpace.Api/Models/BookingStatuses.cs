namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of booking statuses (§8.1). Used by the Bookings status CHECK and by the no_room_overlap exclusion
/// constraint, which covers only <see cref="Active"/> bookings.
/// </summary>
public static class BookingStatuses
{
    public const string Confirmed = "Confirmed";
    public const string CheckedIn = "CheckedIn";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = [Confirmed, CheckedIn, Completed, Cancelled];

    /// <summary>Bookings that hold their room: they block availability and take part in no_room_overlap.</summary>
    public static readonly IReadOnlyList<string> Active = [Confirmed, CheckedIn];
}
