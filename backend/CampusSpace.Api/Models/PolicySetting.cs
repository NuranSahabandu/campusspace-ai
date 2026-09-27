namespace CampusSpace.Api.Models;

/// <summary>
/// One booking-policy value (addendum A.1): the single source of truth for booking limits. Value is text, parsed
/// according to ValueType (opening_hours is JSON). Read only through IPolicySettingsService.
///
/// Departs from the §8 conventions on purpose: the key is a natural text PK (the agents and the policy page use
/// the key, and keys are fixed by migrations), and there is UpdatedAt but no CreatedAt.
/// Not IAuditable: PolicySettingsService writes one audit row per changed key with the old and new value instead.
/// </summary>
public class PolicySetting : IHasUpdatedAt
{
    /// <summary>One of <see cref="PolicyKeys.All"/>.</summary>
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    /// <summary>One of <see cref="PolicyValueTypes.All"/>.</summary>
    public string ValueType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    /// <summary>Null for rows that were seeded and never edited.</summary>
    public long? UpdatedById { get; set; }
    public User? UpdatedBy { get; set; }
}
