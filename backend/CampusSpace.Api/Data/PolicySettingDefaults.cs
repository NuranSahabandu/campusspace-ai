using CampusSpace.Api.Models;

namespace CampusSpace.Api.Data;

/// <summary>
/// The default booking policy (addendum A.1, from plan §3.2, §8.3 and V03/V05/V06/V11). Only the seed and the tests
/// read this list. Runtime code reads the database through IPolicySettingsService and never falls back to these values,
/// because a default in code would be hard-coded policy. The migration inserts the same rows as frozen literals, and a
/// test checks that the two copies agree.
/// </summary>
public static class PolicySettingDefaults
{
    /// <summary>Campus local times; null means closed.</summary>
    public const string OpeningHoursJson =
        """{"mon":{"open":"08:00","close":"20:00"},"tue":{"open":"08:00","close":"20:00"},"wed":{"open":"08:00","close":"20:00"},"thu":{"open":"08:00","close":"20:00"},"fri":{"open":"08:00","close":"20:00"},"sat":{"open":"08:00","close":"16:00"},"sun":null}""";

    public static readonly IReadOnlyList<(string Key, string ValueType, string Value, string Description)> All =
    [
        (PolicyKeys.OpeningHours, PolicyValueTypes.Json, OpeningHoursJson,
            "Opening hours per weekday in campus time (null = closed)"),
        (PolicyKeys.MinLeadTimeHours, PolicyValueTypes.Int, "48",
            "Minimum hours between submitting a request and the booking start"),
        (PolicyKeys.MaxAdvanceDaysStudent, PolicyValueTypes.Int, "60",
            "How many days ahead a student can book"),
        (PolicyKeys.MaxAdvanceDaysLecturer, PolicyValueTypes.Int, "90",
            "How many days ahead a lecturer can book"),
        (PolicyKeys.MaxDurationHours, PolicyValueTypes.Int, "8",
            "Longest booking, in hours"),
        (PolicyKeys.MaxCapacityRatio, PolicyValueTypes.Decimal, "3",
            "A room may seat at most this many times the attendees"),
        (PolicyKeys.SlotGranularityMinutes, PolicyValueTypes.Int, "30",
            "Booking start and end times fall on multiples of this many minutes"),
        (PolicyKeys.FreeCancellationHours, PolicyValueTypes.Int, "24",
            "Cancelling more than this many hours before the start is free; later is flagged as late"),
        (PolicyKeys.MaxOpenRequests, PolicyValueTypes.Int, "3",
            "Most open requests a requester can have at once"),
    ];
}
