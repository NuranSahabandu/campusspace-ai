namespace CampusSpace.Api.Services;

/// <summary>
/// The booking-time rules V05 and V06 (§10.8), and the only place they live. Submit, availability and (Phase 3) the
/// approval re-check all call this, with the current <see cref="PolicySnapshot"/>. Pure: no database, and "now" comes
/// from TimeProvider. Messages match the mobile New request form (time_rules.dart) where the rule is the same.
/// </summary>
public interface IBookingWindowRules
{
    /// <summary>
    /// V05, the rules about the slot itself: start and end on one open campus day, inside its opening hours, on the slot
    /// granularity, and no longer than max_duration_hours. Availability search checks only these.
    /// </summary>
    BookingWindowErrors CheckSlot(DateTimeOffset start, DateTimeOffset end, PolicySnapshot policy);

    /// <summary>
    /// V06, the rules about when the booking is made: start in the future, at least min_lead_time_hours from now, and no
    /// later than the requester role's advance window (max_advance_days_lecturer for a Lecturer, _student otherwise).
    /// </summary>
    BookingWindowErrors CheckTiming(DateTimeOffset start, string requesterRole, PolicySnapshot policy);

    /// <summary>
    /// V06 as it stood at <paramref name="asOf"/> instead of now. The approval re-check passes the request's submission time,
    /// with the CURRENT policy: a requester who submitted on time doesn't fail because the officer was slow, but a policy
    /// change still applies (addendum A.1/A.3 as interpreted in CLAUDE.md). The approval checks "start is still in the
    /// future" against the current clock itself.
    /// </summary>
    BookingWindowErrors CheckTiming(DateTimeOffset start, string requesterRole, PolicySnapshot policy, DateTimeOffset asOf);

    /// <summary>V05 then V06, keeping the first message per field (so lead time is reported last, as on mobile).</summary>
    BookingWindowErrors Check(DateTimeOffset start, DateTimeOffset end, string requesterRole, PolicySnapshot policy);
}

/// <summary>At most one message per field: the first rule that failed. Null means the field passed.</summary>
public sealed record BookingWindowErrors(string? Start, string? End)
{
    public static readonly BookingWindowErrors None = new(null, null);

    public bool IsValid => Start is null && End is null;

    /// <summary>Keeps this result's messages and fills a field that passed from <paramref name="next"/>.</summary>
    public BookingWindowErrors Then(BookingWindowErrors next) => new(Start ?? next.Start, End ?? next.End);

    /// <summary>
    /// The messages as field errors. Submit and approval use the request's field names (the defaults); availability
    /// passes its own query parameter names.
    /// </summary>
    public Dictionary<string, string[]> ToFieldErrors(string startKey = "RequestedStart", string endKey = "RequestedEnd")
    {
        var errors = new Dictionary<string, string[]>();
        if (Start is not null)
            errors[startKey] = [Start];
        if (End is not null)
            errors[endKey] = [End];
        return errors;
    }
}
