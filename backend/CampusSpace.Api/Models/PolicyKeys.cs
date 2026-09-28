namespace CampusSpace.Api.Models;

/// <summary>
/// The PolicySettings keys (addendum A.1) and the value type of each. Used by the CK_PolicySettings_Key CHECK and by
/// PolicySettingsService. Only migrations and the seed create keys; the API can only change their values.
/// </summary>
public static class PolicyKeys
{
    public const string OpeningHours = "opening_hours";
    public const string MinLeadTimeHours = "min_lead_time_hours";
    public const string MaxAdvanceDaysStudent = "max_advance_days_student";
    public const string MaxAdvanceDaysLecturer = "max_advance_days_lecturer";
    public const string MaxDurationHours = "max_duration_hours";
    public const string MaxCapacityRatio = "max_capacity_ratio";
    public const string SlotGranularityMinutes = "slot_granularity_minutes";
    public const string FreeCancellationHours = "free_cancellation_hours";
    public const string MaxOpenRequests = "max_open_requests";
    public const string CheckoutWindowMinutes = "checkout_window_minutes";

    /// <summary>Every key, in the order the policy page lists them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        OpeningHours, MinLeadTimeHours, MaxAdvanceDaysStudent, MaxAdvanceDaysLecturer, MaxDurationHours,
        MaxCapacityRatio, SlotGranularityMinutes, FreeCancellationHours, MaxOpenRequests, CheckoutWindowMinutes,
    ];

    /// <summary>The ValueType each key must have.</summary>
    public static readonly IReadOnlyDictionary<string, string> TypeOf = new Dictionary<string, string>
    {
        [OpeningHours] = PolicyValueTypes.Json,
        [MinLeadTimeHours] = PolicyValueTypes.Int,
        [MaxAdvanceDaysStudent] = PolicyValueTypes.Int,
        [MaxAdvanceDaysLecturer] = PolicyValueTypes.Int,
        [MaxDurationHours] = PolicyValueTypes.Int,
        [MaxCapacityRatio] = PolicyValueTypes.Decimal,
        [SlotGranularityMinutes] = PolicyValueTypes.Int,
        [FreeCancellationHours] = PolicyValueTypes.Int,
        [MaxOpenRequests] = PolicyValueTypes.Int,
        [CheckoutWindowMinutes] = PolicyValueTypes.Int,
    };
}

/// <summary>Values of PolicySettings.ValueType (addendum A.1). No key uses bool yet.</summary>
public static class PolicyValueTypes
{
    public const string Int = "int";
    public const string Decimal = "decimal";
    public const string Bool = "bool";
    public const string Json = "json";

    public static readonly IReadOnlyList<string> All = [Int, Decimal, Bool, Json];
}
