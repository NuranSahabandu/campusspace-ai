namespace CampusSpace.Api.Models;

/// <summary>
/// The hourly room rate for one room type and requester role, from ValidFrom (a campus calendar date) onwards.
/// The rule that prices a booking is the one with the latest ValidFrom on or before the booking's campus start date.
///
/// Price history is immutable: a rule already in effect (ValidFrom &lt;= campus today) can't be edited or deleted;
/// a price change is a new rule with a later ValidFrom. Otherwise an officer changing a price mid-workflow would make
/// V09 (the quote total recomputed by .NET must equal the agent's quote) fail for proposals already in progress,
/// and past quotations could no longer be explained from the rules.
/// </summary>
public class PricingRule : ITimestamped, IAuditable
{
    public long Id { get; set; }
    /// <summary>One of <see cref="RoomTypes.All"/>.</summary>
    public string RoomType { get; set; } = string.Empty;
    /// <summary>One of <see cref="RequesterRoles.All"/>.</summary>
    public string RequesterRole { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    /// <summary>No room charge. The rate must then be 0 (CK_PricingRules_Exempt_ZeroRate).</summary>
    public bool IsExempt { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
