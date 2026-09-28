using System.Collections.Immutable;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

/// <summary>Opening and closing time of one day, in campus local time. Close is exclusive.</summary>
public sealed record DayHours(TimeOnly Open, TimeOnly Close);

/// <summary>
/// The booking policy at one moment (addendum A.1), typed and immutable. IPolicySettingsService.GetAsync builds it from
/// the database on every call. OpeningHours has an entry for every day; null means closed.
/// </summary>
public sealed record PolicySnapshot(
    ImmutableDictionary<DayOfWeek, DayHours?> OpeningHours,
    int MinLeadTimeHours,
    int MaxAdvanceDaysStudent,
    int MaxAdvanceDaysLecturer,
    int MaxDurationHours,
    decimal MaxCapacityRatio,
    int SlotGranularityMinutes,
    int FreeCancellationHours,
    int MaxOpenRequests,
    int CheckoutWindowMinutes)
{
    /// <summary>The JSON day names of opening_hours, Monday first.</summary>
    public static readonly IReadOnlyList<(string Name, DayOfWeek Day)> Days =
    [
        ("mon", DayOfWeek.Monday), ("tue", DayOfWeek.Tuesday), ("wed", DayOfWeek.Wednesday), ("thu", DayOfWeek.Thursday),
        ("fri", DayOfWeek.Friday), ("sat", DayOfWeek.Saturday), ("sun", DayOfWeek.Sunday),
    ];

    /// <summary>
    /// Key to typed value (numbers as numbers, opening_hours as an object with "HH:mm" strings), for
    /// GET /api/policy-settings/public and, later, the agents' policy tool route.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ToPublicValues() => new Dictionary<string, object?>
    {
        [PolicyKeys.OpeningHours] = OpeningHoursJsonShape(),
        [PolicyKeys.MinLeadTimeHours] = MinLeadTimeHours,
        [PolicyKeys.MaxAdvanceDaysStudent] = MaxAdvanceDaysStudent,
        [PolicyKeys.MaxAdvanceDaysLecturer] = MaxAdvanceDaysLecturer,
        [PolicyKeys.MaxDurationHours] = MaxDurationHours,
        [PolicyKeys.MaxCapacityRatio] = MaxCapacityRatio,
        [PolicyKeys.SlotGranularityMinutes] = SlotGranularityMinutes,
        [PolicyKeys.FreeCancellationHours] = FreeCancellationHours,
        [PolicyKeys.MaxOpenRequests] = MaxOpenRequests,
        [PolicyKeys.CheckoutWindowMinutes] = CheckoutWindowMinutes,
    };

    /// <summary>{"mon":{"open":"08:00","close":"20:00"}, ..., "sun":null}, in day order.</summary>
    internal Dictionary<string, object?> OpeningHoursJsonShape() => Days.ToDictionary(
        d => d.Name,
        d => OpeningHours[d.Day] is { } h ? new { open = h.Open.ToString("HH:mm"), close = h.Close.ToString("HH:mm") } : (object?)null);
}
